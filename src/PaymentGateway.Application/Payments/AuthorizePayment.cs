using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payments;

public record AuthorizePaymentCommand(
    string PartyId,
    long Amount,
    string Currency,
    string SettlementChannel) : IRequest<PaymentDto>;

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

        return PaymentDto.FromDomain(payment);
    }
}
