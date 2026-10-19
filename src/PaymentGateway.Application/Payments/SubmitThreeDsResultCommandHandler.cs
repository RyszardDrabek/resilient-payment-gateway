using MediatR;
using PaymentGateway.Application.Metrics;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class SubmitThreeDsResultCommandHandler(
    IPaymentRepository repository,
    ISettlementPort settlementPort) : IRequestHandler<SubmitThreeDsResultCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(SubmitThreeDsResultCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PaymentId);

        var payment = await repository.GetByIdAsync(request.PaymentId, ct)
            ?? throw new PaymentNotFoundException(request.PaymentId);

        if (payment.State != PaymentState.RequiresAction)
        {
            if (payment.State is PaymentState.Authorized or PaymentState.Declined)
            {
                return PaymentMapper.ToDto(payment);
            }

            throw new PaymentInvalidStateException(payment.State, "SubmitThreeDsResult");
        }

        var channel = payment.SettlementChannel;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SettlementResult settlement;

        try
        {
            settlement = await settlementPort.SubmitThreeDsDetailsAsync(
                payment.Id,
                payment.ChannelReference ?? string.Empty,
                request.ThreeDsResult,
                request.Details,
                channel,
                ct);
        }
        catch (Exception ex)
        {
            settlement = SettlementResult.Declined(channel, payment.ChannelReference, $"3DS challenge completion failed: {ex.Message}");
        }
        finally
        {
            sw.Stop();
        }

        payment.CompleteThreeDs(
            settlement.IsAuthorized,
            !string.IsNullOrWhiteSpace(settlement.ChannelReference) ? settlement.ChannelReference : payment.ChannelReference ?? string.Empty,
            settlement.DeclineReason);

        await repository.UpdateAsync(payment, ct);

        PaymentMetrics.RecordTransaction(payment.State.ToString(), channel, "3ds_challenge");
        PaymentMetrics.RecordChannelDuration(sw.Elapsed.TotalSeconds, channel, "3ds_challenge", payment.State.ToString());

        if (payment.State == PaymentState.Declined)
        {
            PaymentMetrics.RecordDecline(payment.DeclineReason ?? "ThreeDsFailed", channel);
        }

        return PaymentMapper.ToDto(payment);
    }
}
