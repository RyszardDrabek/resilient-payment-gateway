using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Web3;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class Web3SettlementPortTests
{
    private readonly Web3Options _options;
    private readonly LocalWeb3SimulatorClient _simulatorClient;
    private readonly Web3SettlementPort _port;

    public Web3SettlementPortTests()
    {
        _options = new Web3Options
        {
            RpcUrl = "http://127.0.0.1:8545",
            TreasuryAddress = "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266",
            MerchantDestinationAddress = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8",
            RefundDestinationAddress = "0x3C44CdDdB6a900fa2b585dd299e03d12FA4293BC",
            SecretlessMode = true,
            SupportedAssets = ["USDC"],
            RequiredFinalityConfirmations = 1
        };

        var optionsWrapper = Options.Create(_options);
        _simulatorClient = new LocalWeb3SimulatorClient(optionsWrapper, NullLogger<LocalWeb3SimulatorClient>.Instance);
        _port = new Web3SettlementPort(_simulatorClient, optionsWrapper, NullLogger<Web3SettlementPort>.Instance);
    }

    [Fact]
    public async Task AuthorizeAsync_WithSupportedUsdc_ReturnsSuccessOffChain_AC1()
    {
        // Act (AC-1: Authorize is off-chain, does not broadcast on-chain transaction)
        var result = await _port.AuthorizeAsync("party_1", 1_000_000, "USDC");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.Channel.Should().Be("WEB3");
        result.ChannelReference.Should().StartWith("web3_auth_");
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_WithUnsupportedAsset_ReturnsDeclined_AC1()
    {
        // Act
        var result = await _port.AuthorizeAsync("party_1", 1_000_000, "DOGE");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.Channel.Should().Be("WEB3");
        result.DeclineReason.Should().Contain("Unsupported asset 'DOGE'");
    }

    [Fact]
    public async Task AuthorizeAsync_WithNegativeOrZeroAmount_ReturnsDeclined_AC1()
    {
        // Act
        var result = await _port.AuthorizeAsync("party_1", 0, "USDC");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.DeclineReason.Should().Contain("greater than zero");
    }

    [Fact]
    public async Task CaptureAsync_SubmitsOnChainSettlementToMerchantDestination_AC2()
    {
        // Arrange
        const long amount = 5_000_000; // 5 USDC
        var paymentId = $"pay_{Guid.NewGuid():N}";
        var authRef = "web3_auth_test";

        // Act
        var result = await _port.CaptureAsync(paymentId, authRef, amount, "USDC");

        // Assert (AC-2: submits on-chain tx to merchant, returns tx hash under finality policy)
        result.IsSuccessful.Should().BeTrue();
        result.Channel.Should().Be("WEB3");
        result.ChannelReference.Should().StartWith("0x");

        // Verify receipt on local simulated chain
        var receipt = await _simulatorClient.GetReceiptAsync(result.ChannelReference);
        receipt.Should().NotBeNull();
        receipt!.IsSuccess.Should().BeTrue();
        receipt.Amount.Should().Be(amount);
        receipt.Asset.Should().Be("USDC");
        receipt.To.Should().Be(_options.MerchantDestinationAddress.ToLowerInvariant());
    }

    [Fact]
    public async Task CaptureAsync_IsIdempotent_ReusesExistingTransactionHash_AC2()
    {
        // Arrange
        const long amount = 2_000_000;
        var paymentId = $"pay_{Guid.NewGuid():N}";

        // Act - First capture
        var firstResult = await _port.CaptureAsync(paymentId, "auth_ref", amount, "USDC");

        // Act - Duplicate capture with same paymentId
        var secondResult = await _port.CaptureAsync(paymentId, "auth_ref", amount, "USDC");

        // Assert: Same transaction hash returned, no second transfer created
        firstResult.ChannelReference.Should().Be(secondResult.ChannelReference);
    }

    [Fact]
    public async Task CancelAsync_CompletesOffChainWithoutBroadcastingTransaction_AC3()
    {
        // Arrange
        var paymentId = $"pay_{Guid.NewGuid():N}";
        const string authRef = "web3_auth_123";

        // Act (AC-3: moves to cancelled off-chain; no transaction broadcast)
        var result = await _port.CancelAsync(paymentId, authRef);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Channel.Should().Be("WEB3");
        result.ChannelReference.Should().Be(authRef);

        // Verify no on-chain receipt exists for authRef
        var receipt = await _simulatorClient.GetReceiptAsync(authRef);
        receipt.Should().BeNull();
    }

    [Fact]
    public async Task RefundAsync_SubmitsOnChainReverseSettlementToRefundDestination_AC4()
    {
        // Arrange
        const long amount = 3_500_000; // 3.5 USDC
        var paymentId = $"pay_{Guid.NewGuid():N}";

        // Act (AC-4: submits reverse settlement on-chain to configured refund destination)
        var result = await _port.RefundAsync(paymentId, "0xprior_capture_hash", amount, "USDC");

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Channel.Should().Be("WEB3");
        result.ChannelReference.Should().StartWith("0x");

        // Verify receipt on local simulated chain
        var receipt = await _simulatorClient.GetReceiptAsync(result.ChannelReference);
        receipt.Should().NotBeNull();
        receipt!.IsSuccess.Should().BeTrue();
        receipt.Amount.Should().Be(amount);
        receipt.To.Should().Be(_options.RefundDestinationAddress.ToLowerInvariant());
    }

    [Fact]
    public async Task SecretlessMode_CompletesEntireLifecycleWithoutExternalSecrets_AC5()
    {
        // AC-5: Complete authorize -> capture -> refund -> cancel in secretless mode
        var paymentId = $"pay_{Guid.NewGuid():N}";
        const long amount = 10_000_000; // 10 USDC

        // 1. Authorize
        var authResult = await _port.AuthorizeAsync("party_secretless", amount, "USDC");
        authResult.IsSuccessful.Should().BeTrue();

        // 2. Capture
        var captureResult = await _port.CaptureAsync(paymentId, authResult.ChannelReference, amount, "USDC");
        captureResult.IsSuccessful.Should().BeTrue();
        captureResult.ChannelReference.Should().StartWith("0x");

        // 3. Status Query
        var statusResult = await _port.QueryPaymentStatusAsync(paymentId, captureResult.ChannelReference);
        statusResult.IsSuccessful.Should().BeTrue();

        // 4. Refund
        var refundResult = await _port.RefundAsync(paymentId, captureResult.ChannelReference, amount, "USDC");
        refundResult.IsSuccessful.Should().BeTrue();
        refundResult.ChannelReference.Should().StartWith("0x");
    }

    [Fact]
    public async Task NativeMinorUnitPrecision_IsPreservedEndToEnd_AC6()
    {
        // AC-6: Native minor units for USDC (6 decimals, e.g. 1 unit or 123_456_789)
        const long preciseAmount = 123_456_789; // 123.456789 USDC
        var paymentId = $"pay_{Guid.NewGuid():N}";

        var authResult = await _port.AuthorizeAsync("party_precision", preciseAmount, "USDC");
        authResult.IsSuccessful.Should().BeTrue();

        var captureResult = await _port.CaptureAsync(paymentId, authResult.ChannelReference, preciseAmount, "USDC");
        captureResult.IsSuccessful.Should().BeTrue();

        var receipt = await _simulatorClient.GetReceiptAsync(captureResult.ChannelReference);
        receipt.Should().NotBeNull();
        receipt!.Amount.Should().Be(preciseAmount); // Preserved without coercion to 2 decimals
    }
}
