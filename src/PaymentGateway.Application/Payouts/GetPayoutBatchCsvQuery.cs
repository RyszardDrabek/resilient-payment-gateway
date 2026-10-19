using System.Text;
using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payouts;

public record PayoutBatchCsvResult(string FileName, string CsvContent);

public record GetPayoutBatchCsvQuery(string BatchId) : IRequest<PayoutBatchCsvResult?>;

public sealed class GetPayoutBatchCsvQueryHandler(IPayoutRepository payoutRepository)
    : IRequestHandler<GetPayoutBatchCsvQuery, PayoutBatchCsvResult?>
{
    public async Task<PayoutBatchCsvResult?> Handle(GetPayoutBatchCsvQuery request, CancellationToken ct)
    {
        var batch = await payoutRepository.GetByIdAsync(request.BatchId, ct);
        if (batch is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Payout Item ID,Payment ID,Captured At (UTC),Gross Amount,Fee Amount,Net Amount,Currency");

        foreach (var item in batch.Items.OrderBy(i => i.CapturedAt))
        {
            sb.AppendLine($"{item.Id},{item.PaymentId},{item.CapturedAt:yyyy-MM-dd HH:mm:ss},{item.GrossAmount},{item.FeeAmount},{item.NetAmount},{item.Currency}");
        }

        sb.AppendLine();
        sb.AppendLine($"# Batch ID: {batch.Id}");
        sb.AppendLine($"# Merchant ID: {batch.MerchantId}");
        sb.AppendLine($"# Period: {batch.PeriodStart:yyyy-MM-dd} to {batch.PeriodEnd:yyyy-MM-dd}");
        sb.AppendLine($"# Total Gross: {batch.GrossAmount} {batch.Currency}");
        sb.AppendLine($"# Total Fees: {batch.FeeAmount} {batch.Currency}");
        sb.AppendLine($"# Total Net: {batch.NetAmount} {batch.Currency}");
        sb.AppendLine($"# Status: {batch.Status}");

        var fileName = $"payout_{batch.MerchantId}_{batch.PeriodStart:yyyyMMdd}_{batch.PeriodEnd:yyyyMMdd}.csv";
        return new PayoutBatchCsvResult(fileName, sb.ToString());
    }
}
