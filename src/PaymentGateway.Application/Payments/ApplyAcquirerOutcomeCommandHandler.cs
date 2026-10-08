using MediatR;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payments;

public sealed class ApplyAcquirerOutcomeCommandHandler(
    IPaymentRepository repository) : IRequestHandler<ApplyAcquirerOutcomeCommand, ApplyAcquirerOutcomeResult>
{
    public async Task<ApplyAcquirerOutcomeResult> Handle(ApplyAcquirerOutcomeCommand request, CancellationToken ct)
    {
        var payment = await repository.GetByIdAsync(request.PaymentId, ct);
        if (payment is null)
        {
            throw new PaymentNotFoundException(request.PaymentId);
        }

        var isApplied = payment.ApplyAcquirerOutcome(request.Outcome, request.ChannelReference, request.Reason);
        if (isApplied)
        {
            await repository.UpdateAsync(payment, ct);
        }

        return new ApplyAcquirerOutcomeResult(
            IsApplied: isApplied,
            Payment: PaymentMapper.ToDto(payment),
            Message: isApplied ? "Acquirer outcome applied." : "Acquirer outcome was ignored (duplicate or superseded by later outcome).");
    }
}
