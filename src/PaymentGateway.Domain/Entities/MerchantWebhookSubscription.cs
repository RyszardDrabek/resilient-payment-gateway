namespace PaymentGateway.Domain.Entities;

public sealed class MerchantWebhookSubscription
{
    public Guid Id { get; private set; }
    public string PartyId { get; private set; } = string.Empty;
    public string EndpointUrl { get; private set; } = string.Empty;
    public string SecretKey { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    private MerchantWebhookSubscription() { } // EF Core

    public MerchantWebhookSubscription(
        Guid id,
        string partyId,
        string endpointUrl,
        string secretKey,
        bool isActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PartyId = partyId;
        EndpointUrl = endpointUrl;
        SecretKey = secretKey;
        IsActive = isActive;
    }

    public void UpdateEndpoint(string endpointUrl, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        EndpointUrl = endpointUrl;
        IsActive = isActive;
    }

    public void RotateSecretKey(string newSecretKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newSecretKey);
        SecretKey = newSecretKey;
    }
}
