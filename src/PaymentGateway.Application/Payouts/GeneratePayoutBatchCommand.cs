using MediatR;
using PaymentGateway.Application.Reconciliation;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payouts;

public record GeneratePayoutBatchCommand(
    string MerchantId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Currency) : IRequest<MerchantPayoutBatchDto>;

public sealed class GeneratePayoutBatchCommandHandler(
    IPaymentRepository paymentRepository,
    IPayoutRepository payoutRepository,
    IFeeCalculator feeCalculator)
    : IRequestHandler<GeneratePayoutBatchCommand, MerchantPayoutBatchDto>
{
    public async Task<MerchantPayoutBatchDto> Handle(GeneratePayoutBatchCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MerchantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);

        if (request.PeriodStart >= request.PeriodEnd)
        {
            throw new ArgumentException("PeriodStart must be earlier than PeriodEnd.");
        }

        var payments = await paymentRepository.GetCapturedPaymentsAsync(
            request.PeriodStart,
            request.PeriodEnd,
            request.MerchantId,
            request.Currency,
            ct);

        // Filter out payments that have already been included in a payout batch
        var paymentIds = payments.Select(p => p.Id).ToList();
        var alreadyPaidOutIds = await payoutRepository.GetPaidOutPaymentIdsAsync(paymentIds, ct);
        var unsettledPayments = payments.Where(p => !alreadyPaidOutIds.Contains(p.Id)).ToList();

        if (unsettledPayments.Count == 0)
        {
            var existingBatches = await payoutRepository.ListByMerchantIdAsync(request.MerchantId, 0, 10, ct);
            var existing = existingBatches.FirstOrDefault(b =>
                b.Currency == request.Currency &&
                b.PeriodStart == request.PeriodStart &&
                b.PeriodEnd == request.PeriodEnd);

            if (existing is not null)
            {
                return existing.ToDto();
            }

            var emptyBatch = new MerchantPayoutBatch(
                $"payout_{Guid.NewGuid():N}",
                request.MerchantId,
                request.PeriodStart,
                request.PeriodEnd,
                request.Currency);
            emptyBatch.MarkCompleted(DateTimeOffset.UtcNow);
            return emptyBatch.ToDto();
        }

        var batch = new MerchantPayoutBatch(
            $"payout_{Guid.NewGuid():N}",
            request.MerchantId,
            request.PeriodStart,
            request.PeriodEnd,
            request.Currency);

        foreach (var payment in unsettledPayments)
        {
            var fee = await feeCalculator.CalculateFeeAsync(
                request.MerchantId,
                request.Currency,
                payment.CapturedAmount,
                payment.CreatedAt,
                ct);

            var item = new PayoutItem(
                $"pi_{Guid.NewGuid():N}",
                batch.Id,
                payment.Id,
                payment.CapturedAmount,
                fee,
                request.Currency,
                payment.CreatedAt);

            batch.AddItem(item);
        }

        batch.MarkCompleted(DateTimeOffset.UtcNow);
        await payoutRepository.SaveAsync(batch, ct);

        return batch.ToDto();
    }
}
