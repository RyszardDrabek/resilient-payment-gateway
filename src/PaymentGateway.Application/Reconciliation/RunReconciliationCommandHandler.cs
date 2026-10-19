using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Reconciliation;

public sealed class RunReconciliationCommandHandler(
    IPaymentRepository paymentRepository,
    IReconciliationRepository reconciliationRepository)
    : IRequestHandler<RunReconciliationCommand, RunReconciliationResult>
{
    public async Task<RunReconciliationResult> Handle(RunReconciliationCommand request, CancellationToken ct)
    {
        var capturedPayments = await paymentRepository.GetCapturedPaymentsAsync(request.From, request.To, ct: ct);
        var records = new List<ReconciliationRecord>();

        foreach (var payment in capturedPayments)
        {
            if (string.IsNullOrWhiteSpace(payment.ChannelReference))
            {
                records.Add(ReconciliationRecord.CreateDiscrepancy(
                    payment.Id,
                    payment.SettlementChannel,
                    channelReference: "UNKNOWN",
                    gatewayAmount: payment.CapturedAmount,
                    acquirerAmount: 0,
                    DiscrepancyType.MissingInAcquirer,
                    "Payment captured in gateway but missing acquirer settlement reference."));
            }
            else
            {
                records.Add(ReconciliationRecord.CreateMatched(
                    payment.Id,
                    payment.SettlementChannel,
                    payment.ChannelReference,
                    payment.CapturedAmount));
            }
        }

        if (records.Count > 0)
        {
            await reconciliationRepository.SaveBatchAsync(records, ct);
        }

        var matchedCount = records.Count(r => r.Status == ReconciliationRecordStatus.Matched);
        var discrepancyCount = records.Count(r => r.Status == ReconciliationRecordStatus.DiscrepancyFlagged);

        return new RunReconciliationResult(records.Count, matchedCount, discrepancyCount);
    }
}
