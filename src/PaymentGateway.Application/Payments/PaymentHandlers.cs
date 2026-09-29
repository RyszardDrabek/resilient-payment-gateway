using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payments;

public record PaymentDto(
    string PaymentId,
    string PartyId,
    long Amount,
    string Currency,
    string SettlementChannel,
    string State,
    string? ChannelReference,
    DateTimeOffset CreatedAt);

public record AuthorizePaymentCommand(
    string PartyId,
    long Amount,
    string Currency,
    string SettlementChannel) : IRequest<PaymentDto>;

public record GetPaymentByIdQuery(string PaymentId) : IRequest<PaymentDto?>;

public sealed class AuthorizePaymentCommandHandler(
    ISettlementPort settlementPort,
    IPaymentRepository repository) : IRequestHandler<AuthorizePaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(AuthorizePaymentCommand request, CancellationToken ct)
    {
        var settlement = await settlementPort.AuthorizeAsync(
            request.PartyId,
            request.Amount,
            request.Currency,
            request.SettlementChannel,
            ct);

        var payment = settlement.IsAuthorized
            ? Payment.Authorize(request.PartyId, request.Amount, request.Currency, request.SettlementChannel, settlement.ChannelReference)
            : Payment.Decline(request.PartyId, request.Amount, request.Currency, request.SettlementChannel, settlement.ChannelReference);

        await repository.AddAsync(payment, ct);

        return ToDto(payment);
    }

    internal static PaymentDto ToDto(Payment p) => new(
        p.Id,
        p.PartyId,
        p.Amount,
        p.Currency,
        p.SettlementChannel,
        p.State.ToString(),
        p.ChannelReference,
        p.CreatedAt);
}

public sealed class GetPaymentByIdQueryHandler(IPaymentRepository repository)
    : IRequestHandler<GetPaymentByIdQuery, PaymentDto?>
{
    public async Task<PaymentDto?> Handle(GetPaymentByIdQuery request, CancellationToken ct)
    {
        var payment = await repository.GetByIdAsync(request.PaymentId, ct);
        return payment is null ? null : AuthorizePaymentCommandHandler.ToDto(payment);
    }
}
