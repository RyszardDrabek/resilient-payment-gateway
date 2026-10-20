using MediatR;
using PaymentGateway.Application.Fx.Models;

namespace PaymentGateway.Application.Fx.Commands;

public sealed record CreateFxQuoteCommand(
    string BaseCurrency,
    long BaseAmount,
    string TargetCurrency,
    string? PartyId = null,
    int? TtlMinutes = null) : IRequest<FxQuoteDto>;
