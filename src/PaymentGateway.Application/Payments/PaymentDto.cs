using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Payments;

public record PaymentDto(
    string PaymentId,
    string PartyId,
    long Amount,
    string Currency,
    string SettlementChannel,
    string State,
    string? ChannelReference,
    DateTimeOffset CreatedAt)
{
    public static PaymentDto FromDomain(Payment p) => new(
        p.Id,
        p.PartyId,
        p.Amount,
        p.Currency,
        p.SettlementChannel,
        p.State.ToString(),
        p.ChannelReference,
        p.CreatedAt);
}
