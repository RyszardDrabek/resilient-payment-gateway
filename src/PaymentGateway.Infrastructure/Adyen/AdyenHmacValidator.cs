using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Adyen;

namespace PaymentGateway.Infrastructure.Adyen;

public sealed class AdyenHmacValidator(IOptions<AdyenOptions> options) : IAdyenHmacValidator
{
    private static readonly Regex HexRegex = new(@"\A[0-9a-fA-F]+\z", RegexOptions.Compiled);
    private readonly AdyenOptions _options = options.Value;

    public bool Validate(AdyenNotificationRequestItem item, string? hmacKeyOverride = null)
    {
        if (item.AdditionalData is null ||
            !item.AdditionalData.TryGetValue("hmacSignature", out var receivedSignature) ||
            string.IsNullOrWhiteSpace(receivedSignature))
        {
            return false;
        }

        var effectiveKey = hmacKeyOverride ?? _options.HmacKey;
        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            return false;
        }

        var expectedSignature = CalculateSignature(item, effectiveKey);

        var receivedBytes = Encoding.UTF8.GetBytes(receivedSignature.Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.Trim());

        if (receivedBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(receivedBytes, expectedBytes);
    }

    public string CalculateSignature(AdyenNotificationRequestItem item, string? hmacKeyOverride = null)
    {
        var effectiveKey = hmacKeyOverride ?? _options.HmacKey
            ?? throw new InvalidOperationException("Adyen HMAC key is not configured.");

        var keyBytes = DecodeKey(effectiveKey);

        var pspRef = Escape(item.PspReference);
        var origRef = Escape(item.OriginalReference);
        var merchantAccount = Escape(item.MerchantAccountCode);
        var merchantRef = Escape(item.MerchantReference);
        var amountVal = Escape(item.Amount?.Value.ToString(CultureInfo.InvariantCulture));
        var amountCurr = Escape(item.Amount?.Currency);
        var eventCode = Escape(item.EventCode);
        var success = Escape(item.Success);

        var dataToSign = string.Join(":",
            pspRef,
            origRef,
            merchantAccount,
            merchantRef,
            amountVal,
            amountCurr,
            eventCode,
            success);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        return Convert.ToBase64String(hash);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace(@"\", @"\\").Replace(":", @"\:");
    }

    private static byte[] DecodeKey(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length % 2 == 0 && HexRegex.IsMatch(trimmed))
        {
            return Convert.FromHexString(trimmed);
        }

        try
        {
            return Convert.FromBase64String(trimmed);
        }
        catch
        {
            return Encoding.UTF8.GetBytes(trimmed);
        }
    }
}
