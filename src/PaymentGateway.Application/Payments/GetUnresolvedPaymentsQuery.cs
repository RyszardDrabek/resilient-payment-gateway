using MediatR;

namespace PaymentGateway.Application.Payments;

public sealed record GetUnresolvedPaymentsQuery(
    string? Channel = null,
    int? AgeMinutes = null,
    int Page = 1,
    int PageSize = 50) : IRequest<UnresolvedPaymentsResponse>;
