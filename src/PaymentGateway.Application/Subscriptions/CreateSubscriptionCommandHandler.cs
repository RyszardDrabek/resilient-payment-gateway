using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Subscriptions;

public sealed class CreateSubscriptionCommandHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentMethodRepository paymentMethodRepository) : IRequestHandler<CreateSubscriptionCommand, SubscriptionDto>
{
    public async Task<SubscriptionDto> Handle(CreateSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var pm = await paymentMethodRepository.GetByIdAsync(request.PaymentMethodId, cancellationToken);
        if (pm is null || pm.Status != PaymentMethodStatus.Active)
        {
            throw new InvalidOperationException($"Payment method '{request.PaymentMethodId}' not found or is revoked.");
        }

        if (pm.IsExpired())
        {
            throw new InvalidOperationException($"Payment method '{request.PaymentMethodId}' is expired.");
        }

        var id = $"sub_{Guid.NewGuid():N}";
        var subscription = new Subscription(
            id,
            request.PartyId,
            request.PaymentMethodId,
            request.Amount,
            request.Currency,
            request.Interval,
            startDate: request.StartDate);

        await subscriptionRepository.SaveAsync(subscription, cancellationToken);

        return ToDto(subscription);
    }

    internal static SubscriptionDto ToDto(Subscription s) => new(
        s.Id,
        s.PartyId,
        s.PaymentMethodId,
        s.Amount,
        s.Currency,
        s.Interval.ToString(),
        s.State.ToString(),
        s.NextBillingAt,
        s.FailureCount,
        s.LastBilledAt,
        s.CreatedAt,
        s.CancelledAt);
}
