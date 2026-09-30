using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payments;

public sealed class AuthorizePaymentCommandHandler(
    ISettlementPort settlementPort,
    IPaymentRepository repository) : IRequestHandler<AuthorizePaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(AuthorizePaymentCommand request, CancellationToken ct)
    {
        SettlementResult settlement;
        try
        {
            settlement = await settlementPort.AuthorizeAsync(
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel,
                ct);
        }
        catch (Exception ex)
        {
            settlement = new SettlementResult(
                false,
                request.SettlementChannel ?? "UNKNOWN",
                string.Empty,
                $"Settlement channel failed to answer: {ex.Message}");
        }

        var channel = settlement.Channel;

        var payment = settlement.IsAuthorized
            ? Payment.Authorize(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference)
            : Payment.Decline(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference, settlement.DeclineReason);

        await repository.AddAsync(payment, ct);

        return PaymentMapper.ToDto(payment);
    }
}
