using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Web3;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Web3;

public sealed class Web3FinalityWatcherService : IWeb3FinalityWatcherService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IWeb3ChainClient _chainClient;
    private readonly Web3Options _options;
    private readonly IMediator _mediator;
    private readonly ILogger<Web3FinalityWatcherService> _logger;

    // Short-lived per-txHash cache (10s TTL) to prevent duplicate chain RPC round-trips
    // when /ops/web3/pending is followed by /ops/web3/watch-all in rapid succession.
    private readonly ConcurrentDictionary<string, (Web3TransactionReceipt? Receipt, DateTimeOffset CachedAt)> _receiptCache = new();
    private static readonly TimeSpan ReceiptCacheTtl = TimeSpan.FromSeconds(10);

    public Web3FinalityWatcherService(
        IPaymentRepository paymentRepository,
        IWeb3ChainClient chainClient,
        IOptions<Web3Options> options,
        IMediator mediator,
        ILogger<Web3FinalityWatcherService> logger)
    {
        _paymentRepository = paymentRepository;
        _chainClient = chainClient;
        _options = options.Value;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Web3SettlementObservation> ObserveAndApplyFinalityAsync(
        string paymentId,
        CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdAsync(paymentId, ct);
        if (payment is null)
        {
            return new Web3SettlementObservation(
                PaymentId: paymentId,
                TransactionHash: string.Empty,
                ConfirmationDepth: 0,
                RequiredConfirmations: _options.RequiredFinalityConfirmations,
                Status: Web3SettlementStatus.Pending,
                Message: $"Payment '{paymentId}' was not found.");
        }

        if (payment.State is PaymentState.Captured or PaymentState.Refunded)
        {
            return new Web3SettlementObservation(
                PaymentId: payment.Id,
                TransactionHash: payment.ChannelReference ?? string.Empty,
                ConfirmationDepth: _options.RequiredFinalityConfirmations,
                RequiredConfirmations: _options.RequiredFinalityConfirmations,
                Status: Web3SettlementStatus.Settled,
                Message: $"Payment is already completed in state '{payment.State}'.");
        }

        if (string.IsNullOrWhiteSpace(payment.ChannelReference))
        {
            return new Web3SettlementObservation(
                PaymentId: payment.Id,
                TransactionHash: string.Empty,
                ConfirmationDepth: 0,
                RequiredConfirmations: _options.RequiredFinalityConfirmations,
                Status: Web3SettlementStatus.Pending,
                Message: "Payment does not have an on-chain transaction hash to observe.");
        }

        var txHash = payment.ChannelReference;
        var receipt = await GetReceiptWithCacheAsync(txHash, ct);

        // AC-3: Reorganized or dropped transaction before finality
        if (receipt is null)
        {
            _logger.LogWarning(
                "Web3 transaction {TxHash} for payment {PaymentId} was dropped or reorganized before finality",
                txHash,
                payment.Id);

            return new Web3SettlementObservation(
                PaymentId: payment.Id,
                TransactionHash: txHash,
                ConfirmationDepth: 0,
                RequiredConfirmations: _options.RequiredFinalityConfirmations,
                Status: Web3SettlementStatus.ReorgPending,
                Message: "Transaction was dropped or reorganized before finality. Payment remains pending.");
        }

        // AC-1, AC-4: Transaction broadcast but has not met required finality confirmations
        if (receipt.Confirmations < _options.RequiredFinalityConfirmations)
        {
            _logger.LogInformation(
                "Web3 transaction {TxHash} for payment {PaymentId} is awaiting finality: {Current}/{Required} confirmations",
                txHash,
                payment.Id,
                receipt.Confirmations,
                _options.RequiredFinalityConfirmations);

            return new Web3SettlementObservation(
                PaymentId: payment.Id,
                TransactionHash: txHash,
                ConfirmationDepth: receipt.Confirmations,
                RequiredConfirmations: _options.RequiredFinalityConfirmations,
                Status: Web3SettlementStatus.Pending,
                Message: $"Transaction awaiting finality confirmation ({receipt.Confirmations}/{_options.RequiredFinalityConfirmations}). Payment remains pending.");
        }

        // Invalidate cache since state transition is about to occur
        _receiptCache.TryRemove(txHash, out _);

        // Identify operation type exclusively via on-chain transfer destination address (receipt.To)
        var isRefund = string.Equals(receipt.To, _options.RefundDestinationAddress, StringComparison.OrdinalIgnoreCase);

        // AC-2, AC-4: Finality policy satisfied
        if (!receipt.IsSuccess)
        {
            // On-chain revert: restore prior committed state (Authorized for capture, Captured for refund)
            var priorOutcome = isRefund
                ? PaymentLifecycleOutcome.Captured
                : PaymentLifecycleOutcome.Authorized;

            _logger.LogWarning(
                "Web3 transaction {TxHash} for payment {PaymentId} reverted on-chain. Restoring prior state {PriorState}",
                txHash,
                payment.Id,
                priorOutcome);

            await _mediator.Send(new ApplyAcquirerOutcomeCommand(
                PaymentId: payment.Id,
                Outcome: priorOutcome,
                ChannelReference: txHash,
                Reason: "On-chain transaction reverted."), ct);

            return new Web3SettlementObservation(
                PaymentId: payment.Id,
                TransactionHash: txHash,
                ConfirmationDepth: receipt.Confirmations,
                RequiredConfirmations: _options.RequiredFinalityConfirmations,
                Status: Web3SettlementStatus.Settled,
                Message: "Transaction reverted on chain; restored prior committed state.");
        }

        // Finalized and successful -> complete corresponding transition (AC-2)
        var targetOutcome = isRefund
            ? PaymentLifecycleOutcome.Refunded
            : PaymentLifecycleOutcome.Captured;

        _logger.LogInformation(
            "Web3 transaction {TxHash} for payment {PaymentId} met finality ({Confirmations} confs). Transitioning to {TargetOutcome}",
            txHash,
            payment.Id,
            receipt.Confirmations,
            targetOutcome);

        await _mediator.Send(new ApplyAcquirerOutcomeCommand(
            PaymentId: payment.Id,
            Outcome: targetOutcome,
            ChannelReference: txHash), ct);

        return new Web3SettlementObservation(
            PaymentId: payment.Id,
            TransactionHash: txHash,
            ConfirmationDepth: receipt.Confirmations,
            RequiredConfirmations: _options.RequiredFinalityConfirmations,
            Status: Web3SettlementStatus.Settled,
            Message: $"Finality policy satisfied ({receipt.Confirmations} confirmations). Transitioned to {targetOutcome}.");
    }

    public async Task<IReadOnlyList<Web3SettlementObservation>> WatchAllPendingSettlementsAsync(CancellationToken ct = default)
    {
        var unresolved = await _paymentRepository.GetUnresolvedAsync(ct);
        var web3Pending = unresolved
            .Where(p => string.Equals(p.SettlementChannel, Web3SettlementPort.ChannelName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(p.ChannelReference))
            .ToList();

        var results = new List<Web3SettlementObservation>();
        foreach (var payment in web3Pending)
        {
            var obs = await ObserveAndApplyFinalityAsync(payment.Id, ct);
            results.Add(obs);
        }

        return results;
    }

    public async Task<IReadOnlyList<Web3SettlementObservation>> GetPendingSettlementsAsync(CancellationToken ct = default)
    {
        var unresolved = await _paymentRepository.GetUnresolvedAsync(ct);
        var web3Pending = unresolved
            .Where(p => string.Equals(p.SettlementChannel, Web3SettlementPort.ChannelName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new List<Web3SettlementObservation>();
        foreach (var payment in web3Pending)
        {
            if (string.IsNullOrWhiteSpace(payment.ChannelReference))
            {
                results.Add(new Web3SettlementObservation(
                    PaymentId: payment.Id,
                    TransactionHash: string.Empty,
                    ConfirmationDepth: 0,
                    RequiredConfirmations: _options.RequiredFinalityConfirmations,
                    Status: Web3SettlementStatus.Pending,
                    Message: "Pending without transaction hash."));
                continue;
            }

            var receipt = await GetReceiptWithCacheAsync(payment.ChannelReference, ct);
            if (receipt is null)
            {
                results.Add(new Web3SettlementObservation(
                    PaymentId: payment.Id,
                    TransactionHash: payment.ChannelReference,
                    ConfirmationDepth: 0,
                    RequiredConfirmations: _options.RequiredFinalityConfirmations,
                    Status: Web3SettlementStatus.ReorgPending,
                    Message: "Receipt not found or reorganized before finality."));
            }
            else
            {
                var isFinalized = receipt.Confirmations >= _options.RequiredFinalityConfirmations;
                results.Add(new Web3SettlementObservation(
                    PaymentId: payment.Id,
                    TransactionHash: payment.ChannelReference,
                    ConfirmationDepth: receipt.Confirmations,
                    RequiredConfirmations: _options.RequiredFinalityConfirmations,
                    Status: isFinalized ? Web3SettlementStatus.Settled : Web3SettlementStatus.Pending,
                    Message: isFinalized ? "Finality met; awaiting watcher pass." : "Awaiting finality confirmations."));
            }
        }

        return results;
    }

    private async Task<Web3TransactionReceipt?> GetReceiptWithCacheAsync(string txHash, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_receiptCache.TryGetValue(txHash, out var entry) && now - entry.CachedAt < ReceiptCacheTtl)
        {
            return entry.Receipt;
        }

        var receipt = await _chainClient.GetReceiptAsync(txHash, ct);
        _receiptCache[txHash] = (receipt, now);
        return receipt;
    }
}
