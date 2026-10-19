using MediatR;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Subscriptions;

public record CreateSubscriptionCommand(
    string PartyId,
    string PaymentMethodId,
    long Amount,
    string Currency,
    SubscriptionInterval Interval,
    DateTimeOffset? StartDate = null) : IRequest<SubscriptionDto>;
