using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class ReplayMerchantWebhookCommandHandler(
    IMerchantWebhookDeadLetterRepository dlqRepository,
    IMerchantWebhookSubscriptionRepository subscriptionRepository,
    IMerchantWebhookSigner signer,
    HttpClient httpClient,
    ILogger<ReplayMerchantWebhookCommandHandler>? logger = null,
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

        var secretKey = subscription?.SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            logger?.LogWarning(
                "Replaying webhook for DLQ entry {DlqEntryId} (merchant {MerchantId}) without active webhook subscription or secret key; falling back to default signing key.",
                command.DlqEntryId,
                command.MerchantId);
            secretKey = "webhook-secret";
        }

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
                logger?.LogInformation(
                    "Webhook replayed successfully for DLQ entry {DlqEntryId} (merchant {MerchantId}, status code {StatusCode}).",
                    command.DlqEntryId,
                    command.MerchantId,
                    (int)response.StatusCode);

                record.RecordReplayAttempt(successful: true, statusCode: (int)response.StatusCode, timestamp: _timeProvider.GetUtcNow());
                await dlqRepository.UpdateAsync(record, ct);
                return new ReplayMerchantWebhookResult(
                    Success: true,
                    Message: "Webhook replayed successfully.",
                    StatusCode: (int)response.StatusCode,
                    ReplayedAt: record.ReplayedAt);
            }

            logger?.LogWarning(
                "Webhook replay failed for DLQ entry {DlqEntryId} (merchant {MerchantId}, HTTP {StatusCode} {ReasonPhrase}).",
                command.DlqEntryId,
                command.MerchantId,
                (int)response.StatusCode,
                response.ReasonPhrase);

            record.RecordReplayAttempt(successful: false, statusCode: (int)response.StatusCode);
            await dlqRepository.UpdateAsync(record, ct);

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

            logger?.LogWarning(
                ex,
                "Webhook replay encountered exception for DLQ entry {DlqEntryId} (merchant {MerchantId}): {Message}",
                command.DlqEntryId,
                command.MerchantId,
                ex.Message);

            record.RecordReplayAttempt(successful: false, statusCode: null);
            try
            {
                await dlqRepository.UpdateAsync(record, CancellationToken.None);
            }
            catch (Exception updateEx)
            {
                logger?.LogWarning(
                    updateEx,
                    "Failed to persist replay attempt status for DLQ entry {DlqEntryId}.",
                    command.DlqEntryId);
            }

            return new ReplayMerchantWebhookResult(
                Success: false,
                Message: $"Replay exception: {ex.Message}");
        }
    }
}
