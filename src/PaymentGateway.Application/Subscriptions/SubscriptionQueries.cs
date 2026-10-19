using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Subscriptions;

public record GetSubscriptionByIdQuery(string Id) : IRequest<SubscriptionDto?>;

public record ListSubscriptionsQuery(
    string? PartyId = null,
    int Skip = 0,
    int Take = 50) : IRequest<IReadOnlyList<SubscriptionDto>>;

public sealed class SubscriptionQueryHandlers(
    ISubscriptionRepository repository) :
    IRequestHandler<GetSubscriptionByIdQuery, SubscriptionDto?>,
    IRequestHandler<ListSubscriptionsQuery, IReadOnlyList<SubscriptionDto>>
{
    public async Task<SubscriptionDto?> Handle(GetSubscriptionByIdQuery request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(request.Id, cancellationToken);
        return item is not null ? CreateSubscriptionCommandHandler.ToDto(item) : null;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> Handle(ListSubscriptionsQuery request, CancellationToken cancellationToken)
    {
        var items = !string.IsNullOrWhiteSpace(request.PartyId)
            ? await repository.ListByPartyIdAsync(request.PartyId, cancellationToken)
            : await repository.ListAsync(request.Skip, request.Take, cancellationToken);

        return items.Select(CreateSubscriptionCommandHandler.ToDto).ToList();
    }
}
