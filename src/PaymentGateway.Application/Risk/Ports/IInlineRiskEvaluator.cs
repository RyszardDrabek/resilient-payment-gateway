using PaymentGateway.Application.Risk.Models;

namespace PaymentGateway.Application.Risk.Ports;

public interface IInlineRiskEvaluator
{
    Task<InlineRiskEvaluation> EvaluateAsync(
        string partyId,
        long amount,
        string currency,
        string? settlementChannel = null,
        string? paymentMethodId = null,
        CancellationToken ct = default);
}
