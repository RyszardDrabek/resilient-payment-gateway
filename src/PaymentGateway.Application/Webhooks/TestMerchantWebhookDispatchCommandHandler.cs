using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class TestMerchantWebhookDispatchCommandHandler(
    IMerchantWebhookSubscriptionRepository repository,
    IMerchantWebhookSimulator simulator)
    : IRequestHandler<TestMerchantWebhookDispatchCommand, TestMerchantWebhookDispatchResultDto>
{
    public async Task<TestMerchantWebhookDispatchResultDto> Handle(
        TestMerchantWebhookDispatchCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PartyId);

        var subscription = await repository.GetByPartyIdAsync(request.PartyId, cancellationToken);
        var endpointUrl = request.EndpointUrl ?? subscription?.EndpointUrl;

        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            throw new InvalidOperationException("No webhook endpoint configured. Please specify an endpoint URL.");
        }

        var secretKey = subscription?.SecretKey ?? "whsec_dev_fallback_secret_key_1234567890";

        return await simulator.SimulateDispatchAsync(
            endpointUrl,
            secretKey,
            request.EventType,
            cancellationToken);
    }
}
