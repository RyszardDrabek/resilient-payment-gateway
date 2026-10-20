using MediatR;
using PaymentGateway.Application.Fx.Models;

namespace PaymentGateway.Application.Fx.Queries;

public sealed record GetFxQuoteByIdQuery(string QuoteId) : IRequest<FxQuoteDto?>;
