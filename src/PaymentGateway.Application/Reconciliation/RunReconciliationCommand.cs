using MediatR;

namespace PaymentGateway.Application.Reconciliation;

public record RunReconciliationCommand(
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<RunReconciliationResult>;

public record RunReconciliationResult(
    int TotalProcessed,
    int MatchedCount,
    int DiscrepancyCount);
