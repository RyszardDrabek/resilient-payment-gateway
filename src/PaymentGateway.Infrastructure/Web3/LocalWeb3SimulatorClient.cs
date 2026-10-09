using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Infrastructure.Web3;

public sealed class LocalWeb3SimulatorClient : IWeb3ChainClient
{
    private readonly Web3Options _options;
    private readonly ILogger<LocalWeb3SimulatorClient> _logger;

    // Idempotency: token -> txHash
    private readonly ConcurrentDictionary<string, string> _idempotentBroadcasts = new();

    // Stored transaction receipts: txHash -> Web3TransactionReceipt
    private readonly ConcurrentDictionary<string, Web3TransactionReceipt> _receipts = new();

    // Balances: (address, asset) -> balance
    private readonly ConcurrentDictionary<(string Address, string Asset), long> _balances = new();

    public LocalWeb3SimulatorClient(
        IOptions<Web3Options> options,
        ILogger<LocalWeb3SimulatorClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Initialize prefunded treasury balance for demo / secretless test execution (e.g. 100,000,000 USDC)
        foreach (var asset in _options.SupportedAssets)
        {
            _balances[(_options.TreasuryAddress.ToLowerInvariant(), asset.ToUpperInvariant())] = 100_000_000_000_000;
        }
    }

    public Task<string> BroadcastTransferAsync(
        string fromAddress,
        string toAddress,
        long amount,
        string asset,
        string? idempotencyToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);

        var normFrom = fromAddress.ToLowerInvariant();
        var normTo = toAddress.ToLowerInvariant();
        var normAsset = asset.ToUpperInvariant();

        // On-chain idempotency (ADR-010): check if token was already broadcast
        if (!string.IsNullOrWhiteSpace(idempotencyToken) &&
            _idempotentBroadcasts.TryGetValue(idempotencyToken, out var existingHash))
        {
            _logger.LogInformation(
                "Idempotent Web3 broadcast replay for token {Token}: returning tx {TxHash}",
                idempotencyToken,
                existingHash);
            return Task.FromResult(existingHash);
        }

        // Generate deterministic or pseudo-unique on-chain tx hash
        string txHash;
        if (!string.IsNullOrWhiteSpace(idempotencyToken))
        {
            using var sha = SHA256.Create();
            var raw = Encoding.UTF8.GetBytes($"{normFrom}:{normTo}:{amount}:{normAsset}:{idempotencyToken}");
            var hashBytes = sha.ComputeHash(raw);
            txHash = "0x" + Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        else
        {
            txHash = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        }

        // Adjust balances
        var fromKey = (normFrom, normAsset);
        var toKey = (normTo, normAsset);

        _balances.AddOrUpdate(fromKey, _ => 0 - amount, (_, curr) => curr - amount);
        _balances.AddOrUpdate(toKey, _ => amount, (_, curr) => curr + amount);

        var confirmations = _options.InitialConfirmations ?? Math.Max(1, _options.RequiredFinalityConfirmations);

        var receipt = new Web3TransactionReceipt(
            TransactionHash: txHash,
            From: normFrom,
            To: normTo,
            Amount: amount,
            Asset: normAsset,
            IsSuccess: true,
            Confirmations: confirmations,
            BlockTimestamp: DateTimeOffset.UtcNow);

        _receipts[txHash] = receipt;

        if (!string.IsNullOrWhiteSpace(idempotencyToken))
        {
            _idempotentBroadcasts.TryAdd(idempotencyToken, txHash);
        }

        _logger.LogInformation(
            "Web3 simulated transfer broadcast: {Amount} {Asset} from {From} to {To}, txHash={TxHash}, confirmations={Confirmations}",
            amount,
            normAsset,
            normFrom,
            normTo,
            txHash,
            confirmations);

        return Task.FromResult(txHash);
    }

    public Task<Web3TransactionReceipt?> GetReceiptAsync(
        string transactionHash,
        CancellationToken ct = default)
    {
        _receipts.TryGetValue(transactionHash, out var receipt);
        return Task.FromResult(receipt);
    }

    public Task<bool> IsFinalizedAsync(
        string transactionHash,
        int requiredConfirmations,
        CancellationToken ct = default)
    {
        if (_receipts.TryGetValue(transactionHash, out var receipt))
        {
            return Task.FromResult(receipt.IsSuccess && receipt.Confirmations >= requiredConfirmations);
        }

        return Task.FromResult(false);
    }

    public void SetConfirmations(string transactionHash, int confirmations)
    {
        if (_receipts.TryGetValue(transactionHash, out var existing))
        {
            _receipts[transactionHash] = existing with { Confirmations = confirmations };
            _logger.LogInformation("Web3 simulator updated confirmations for {TxHash}: {Confirmations}", transactionHash, confirmations);
        }
    }

    public void DropReceipt(string transactionHash)
    {
        _receipts.TryRemove(transactionHash, out _);
        _logger.LogWarning("Web3 simulator dropped receipt for {TxHash} (simulating drop/reorg)", transactionHash);
    }

    public void SimulateReorg(string transactionHash)
    {
        DropReceipt(transactionHash);
    }

    public void SimulateRevert(string transactionHash)
    {
        if (_receipts.TryGetValue(transactionHash, out var existing))
        {
            _receipts[transactionHash] = existing with { IsSuccess = false };
            _logger.LogWarning("Web3 simulator marked {TxHash} as reverted on chain", transactionHash);
        }
    }

    public long GetBalance(string address, string asset)
    {
        _balances.TryGetValue((address.ToLowerInvariant(), asset.ToUpperInvariant()), out var bal);
        return bal;
    }
}
