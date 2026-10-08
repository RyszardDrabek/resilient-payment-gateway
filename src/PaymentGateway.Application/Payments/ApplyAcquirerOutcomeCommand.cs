using MediatR;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Application.Payments;

public sealed record ApplyAcquirerOutcomeCommand(
    string PaymentId,
    PaymentLifecycleOutcome Outcome,
    string? ChannelReference = null,
    string? Reason = null) : IRequest<ApplyAcquirerOutcomeResult>;

public sealed record ApplyAcquirerOutcomeResult(
    bool IsApplied,
    PaymentDto? Payment,
    string? Message = null);
