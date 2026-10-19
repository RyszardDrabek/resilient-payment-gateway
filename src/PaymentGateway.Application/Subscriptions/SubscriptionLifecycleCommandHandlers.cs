using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Subscriptions;

public sealed class SubscriptionLifecycleCommandHandlers(
    ISubscriptionRepository repository) :
    IRequestHandler<PauseSubscriptionCommand, SubscriptionDto>,
    IRequestHandler<ResumeSubscriptionCommand, SubscriptionDto>,
    IRequestHandler<CancelSubscriptionCommand, SubscriptionDto>
{
    public async Task<SubscriptionDto> Handle(PauseSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Subscription '{request.Id}' was not found.");

        subscription.Pause();
        await repository.SaveAsync(subscription, cancellationToken);
        return CreateSubscriptionCommandHandler.ToDto(subscription);
    }

    public async Task<SubscriptionDto> Handle(ResumeSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Subscription '{request.Id}' was not found.");

        subscription.Resume();
        await repository.SaveAsync(subscription, cancellationToken);
        return CreateSubscriptionCommandHandler.ToDto(subscription);
    }

    public async Task<SubscriptionDto> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Subscription '{request.Id}' was not found.");

        subscription.Cancel();
        await repository.SaveAsync(subscription, cancellationToken);
        return CreateSubscriptionCommandHandler.ToDto(subscription);
    }
}
