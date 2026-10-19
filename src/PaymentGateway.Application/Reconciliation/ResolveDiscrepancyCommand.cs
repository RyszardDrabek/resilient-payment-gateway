using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Reconciliation;

public record ResolveDiscrepancyCommand(
    string RecordId,
    string? ResolutionNotes = null) : IRequest<bool>;

public sealed class ResolveDiscrepancyCommandHandler(IReconciliationRepository reconciliationRepository)
    : IRequestHandler<ResolveDiscrepancyCommand, bool>
{
    public async Task<bool> Handle(ResolveDiscrepancyCommand request, CancellationToken ct)
    {
        var record = await reconciliationRepository.GetByIdAsync(request.RecordId, ct);
        if (record is null)
        {
            return false;
        }

        record.Resolve(request.ResolutionNotes);
        await reconciliationRepository.SaveAsync(record, ct);
        return true;
    }
}
