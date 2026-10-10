using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Web3;

namespace PaymentGateway.Infrastructure.Services;

public sealed class RoutingSettlementPort : ISettlementPort
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _configuredActiveChannel;

    public RoutingSettlementPort(IServiceProvider serviceProvider, IConfiguration? configuration = null)
    {
        _serviceProvider = serviceProvider;
        _configuredActiveChannel = configuration?["PaymentGateway:ActiveChannel"] ?? configuration?["Settlement:ChannelId"];
    }

    private ISettlementPort ResolveByChannelName(string? channel)
    {
        if (string.Equals(channel, "WEB3", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<Web3SettlementPort>();
        }

        if (string.Equals(channel, "MOCK", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<MockSettlementPort>();
        }

        return _serviceProvider.GetRequiredService<AdyenSettlementPort>();
    }

    private ISettlementPort Resolve(string? channel)
        => !string.IsNullOrWhiteSpace(channel)
            ? ResolveByChannelName(channel)
            : ResolveByChannelName(_configuredActiveChannel);

    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
        => Resolve(channel).AuthorizeAsync(partyId, amount, currency, channel, ct);

    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel,
        string? idempotencyKey,
        CancellationToken ct = default)
        => Resolve(channel).AuthorizeAsync(partyId, amount, currency, channel, idempotencyKey, ct);

    public Task<SettlementResult> CaptureAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
        => Resolve(channel).CaptureAsync(paymentId, channelReference, amount, currency, channel, ct);

    public Task<SettlementResult> CancelAsync(
        string paymentId,
        string channelReference,
        string? channel = null,
        CancellationToken ct = default)
        => Resolve(channel).CancelAsync(paymentId, channelReference, channel, ct);

    public Task<SettlementResult> RefundAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
        => Resolve(channel).RefundAsync(paymentId, channelReference, amount, currency, channel, ct);

    public Task<SettlementResult> QueryPaymentStatusAsync(
        string paymentId,
        string? channelReference = null,
        string? merchantReference = null,
        string? channel = null,
        CancellationToken ct = default)
        => Resolve(channel).QueryPaymentStatusAsync(paymentId, channelReference, merchantReference, channel, ct);
}
