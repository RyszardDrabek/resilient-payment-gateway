using System.Text;
using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class ReplayMerchantWebhookCommandHandler(
    IMerchantWebhookDeadLetterRepository dlqRepository,
    IMerchantWebhookSubscriptionRepository subscriptionRepository,
    IMerchantWebhookSigner signer,
    HttpClient httpClient,
    TimeProvider? timeProvider = null)
    : IRequestHandler<ReplayMerchantWebhookCommand, ReplayMerchantWebhookResult?>
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ReplayMerchantWebhookResult?> Handle(
        ReplayMerchantWebhookCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var record = await dlqRepository.GetByIdAsync(command.DlqEntryId, ct);
        if (record is null || !string.Equals(record.MerchantId, command.MerchantId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var subscription = await subscriptionRepository.GetActiveByPartyIdAsync(command.MerchantId, ct)
                           ?? await subscriptionRepository.GetByPartyIdAsync(command.MerchantId, ct);

        var secretKey = subscription?.SecretKey ?? "webhook-secret";
        var signature = signer.ComputeSignature(record.PayloadJson, secretKey);
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, record.DestinationUrl)
        {
            Content = new StringContent(record.PayloadJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("X-Signature-SHA256", signature);
        request.Headers.Add("X-Event-Id", record.EventId);
        request.Headers.Add("X-Timestamp", timestamp);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                record.MarkReplayed();
                await dlqRepository.UpdateAsync(record, ct);
                return new ReplayMerchantWebhookResult(
                    Success: true,
                    Message: "Webhook replayed successfully.",
                    StatusCode: (int)response.StatusCode,
                    ReplayedAt: record.ReplayedAt);
            }

            return new ReplayMerchantWebhookResult(
                Success: false,
                Message: $"Replay failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                StatusCode: (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return new ReplayMerchantWebhookResult(
                Success: false,
                Message: $"Replay exception: {ex.Message}");
        }
    }
}
