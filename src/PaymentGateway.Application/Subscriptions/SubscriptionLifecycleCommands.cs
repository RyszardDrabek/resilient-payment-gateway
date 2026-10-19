using MediatR;

namespace PaymentGateway.Application.Subscriptions;

public record PauseSubscriptionCommand(string Id) : IRequest<SubscriptionDto>;
public record ResumeSubscriptionCommand(string Id) : IRequest<SubscriptionDto>;
public record CancelSubscriptionCommand(string Id) : IRequest<SubscriptionDto>;
