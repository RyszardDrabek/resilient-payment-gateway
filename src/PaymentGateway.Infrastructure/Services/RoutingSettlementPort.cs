using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Web3;

namespace PaymentGateway.Infrastructure.Services;

public sealed class RoutingSettlementPort : ISettlementPort
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public RoutingSettlementPort(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    private ISettlementPort Resolve(string? channel)
    {
        if (string.Equals(channel, "WEB3", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<Web3SettlementPort>();
        }

        if (string.Equals(channel, "MOCK", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<MockSettlementPort>();
        }

        if (string.Equals(channel, "ADYEN", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<AdyenSettlementPort>();
        }

        var activeChannel = _configuration["PaymentGateway:ActiveChannel"] ?? _configuration["Settlement:ChannelId"];
        if (string.Equals(activeChannel, "WEB3", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<Web3SettlementPort>();
        }

        if (string.Equals(activeChannel, "MOCK", StringComparison.OrdinalIgnoreCase))
        {
            return _serviceProvider.GetRequiredService<MockSettlementPort>();
        }

        return _serviceProvider.GetRequiredService<AdyenSettlementPort>();
    }

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
}
