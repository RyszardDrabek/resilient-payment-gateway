using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Web3;

public sealed class Web3SettlementPort : ISettlementPort
{
    public const string ChannelName = "WEB3";

    private readonly IWeb3ChainClient _chainClient;
    private readonly Web3Options _options;
    private readonly ILogger<Web3SettlementPort> _logger;

    public Web3SettlementPort(
        IWeb3ChainClient chainClient,
        IOptions<Web3Options> options,
        ILogger<Web3SettlementPort> logger)
    {
        _chainClient = chainClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default) =>
        AuthorizeAsync(partyId, amount, currency, channel, null, ct);

    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        // AC-6 / AC-1: Validate supported crypto asset
        var isSupported = _options.SupportedAssets.Any(a => string.Equals(a, currency, StringComparison.OrdinalIgnoreCase));
        if (!isSupported)
        {
            _logger.LogWarning("Web3 authorization declined: unsupported asset '{Currency}'", currency);
            return Task.FromResult(SettlementResult.Declined(
                ChannelName,
                string.Empty,
                $"Unsupported asset '{currency}'. Supported assets: {string.Join(", ", _options.SupportedAssets)}."));
        }

        if (amount <= 0)
        {
            _logger.LogWarning("Web3 authorization declined: invalid amount {Amount}", amount);
            return Task.FromResult(SettlementResult.Declined(
                ChannelName,
                string.Empty,
                "Payment amount must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(_options.MerchantDestinationAddress))
        {
            _logger.LogError("Web3 authorization declined: merchant destination address is not configured");
            return Task.FromResult(SettlementResult.Declined(
                ChannelName,
                string.Empty,
                "Merchant destination address is not configured."));
        }

        // AC-1: Authorize stays off-chain without broadcasting an on-chain transaction
        var channelReference = $"web3_auth_{Guid.NewGuid():N}";
        _logger.LogInformation(
            "Web3 authorization approved off-chain for party {PartyId}, amount {Amount} {Currency}, channelRef {ChannelRef}",
            partyId,
            amount,
            currency,
            channelReference);

        return Task.FromResult(SettlementResult.Success(ChannelName, channelReference));
    }

    public async Task<SettlementResult> CaptureAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        try
        {
            // AC-2: On-chain settlement for full amount to merchant destination
            var idempotencyToken = $"capture:{paymentId}";
            var txHash = await _chainClient.BroadcastTransferAsync(
                _options.TreasuryAddress,
                _options.MerchantDestinationAddress,
                amount,
                currency,
                idempotencyToken,
                ct);

            // Finality policy check (AC-2)
            var isFinalized = await _chainClient.IsFinalizedAsync(
                txHash,
                _options.RequiredFinalityConfirmations,
                ct);

            if (!isFinalized)
            {
                _logger.LogWarning(
                    "Web3 capture broadcast {TxHash} for payment {PaymentId} is awaiting finality",
                    txHash,
                    paymentId);
                return SettlementResult.Unanswered(ChannelName, "Transaction awaiting finality confirmation.", txHash);
            }

            _logger.LogInformation(
                "Web3 capture confirmed on-chain: txHash={TxHash} for payment {PaymentId}",
                txHash,
                paymentId);

            return SettlementResult.Success(ChannelName, txHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web3 capture failed for payment {PaymentId}", paymentId);
            return SettlementResult.Declined(ChannelName, channelReference, $"Web3 capture transfer failed: {ex.Message}");
        }
    }

    public Task<SettlementResult> CancelAsync(
        string paymentId,
        string channelReference,
        string? channel = null,
        CancellationToken ct = default)
    {
        // AC-3: Cancel while authorized/uncaptured moves to cancelled off-chain; NO transaction broadcast
        _logger.LogInformation(
            "Web3 cancel completed off-chain for payment {PaymentId}, channelRef {ChannelRef}",
            paymentId,
            channelReference);

        return Task.FromResult(SettlementResult.Success(ChannelName, channelReference));
    }

    public async Task<SettlementResult> RefundAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        try
        {
            // AC-4: On-chain reverse settlement for full amount to refund destination
            var idempotencyToken = $"refund:{paymentId}";
            var txHash = await _chainClient.BroadcastTransferAsync(
                _options.TreasuryAddress,
                _options.RefundDestinationAddress,
                amount,
                currency,
                idempotencyToken,
                ct);

            var isFinalized = await _chainClient.IsFinalizedAsync(
                txHash,
                _options.RequiredFinalityConfirmations,
                ct);

            if (!isFinalized)
            {
                _logger.LogWarning(
                    "Web3 refund broadcast {TxHash} for payment {PaymentId} is awaiting finality",
                    txHash,
                    paymentId);
                return SettlementResult.Unanswered(ChannelName, "Transaction awaiting finality confirmation.", txHash);
            }

            _logger.LogInformation(
                "Web3 refund confirmed on-chain: txHash={TxHash} for payment {PaymentId}",
                txHash,
                paymentId);

            return SettlementResult.Success(ChannelName, txHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web3 refund failed for payment {PaymentId}", paymentId);
            return SettlementResult.Declined(ChannelName, channelReference, $"Web3 refund transfer failed: {ex.Message}");
        }
    }

    public async Task<SettlementResult> QueryPaymentStatusAsync(
        string paymentId,
        string? channelReference = null,
        string? merchantReference = null,
        string? channel = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelReference))
        {
            return SettlementResult.Unanswered(ChannelName, "Missing transaction hash for status query.");
        }

        var receipt = await _chainClient.GetReceiptAsync(channelReference, ct);
        if (receipt is null)
        {
            return SettlementResult.Unanswered(ChannelName, "Transaction receipt not found on chain.", channelReference);
        }

        if (!receipt.IsSuccess)
        {
            return SettlementResult.Declined(ChannelName, receipt.TransactionHash, "Transaction reverted on chain.");
        }

        if (receipt.Confirmations < _options.RequiredFinalityConfirmations)
        {
            return SettlementResult.Unanswered(
                ChannelName,
                $"Transaction awaiting finality confirmation ({receipt.Confirmations}/{_options.RequiredFinalityConfirmations}).",
                receipt.TransactionHash);
        }

        return SettlementResult.Success(ChannelName, receipt.TransactionHash);
    }
}
