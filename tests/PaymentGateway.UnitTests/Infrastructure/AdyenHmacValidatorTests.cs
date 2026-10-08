using FluentAssertions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Infrastructure.Adyen;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class AdyenHmacValidatorTests
{
    private const string TestHexKey = "44782DEF547AAB80616F310827471246151F9957283A359CE97849303DE8E9E3";
    private readonly AdyenHmacValidator _validator;

    public AdyenHmacValidatorTests()
    {
        var options = Options.Create(new AdyenOptions
        {
            HmacKey = TestHexKey
        });
        _validator = new AdyenHmacValidator(options);
    }

    [Fact]
    public void CalculateSignature_And_Validate_WithValidItem_ReturnsTrue()
    {
        var item = new AdyenNotificationRequestItem(
            AdditionalData: new Dictionary<string, string>(),
            Amount: new AdyenAmountDto("EUR", 1000),
            EventCode: "AUTHORISATION",
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "TestAccount",
            MerchantReference: "PAY-12345",
            OriginalReference: null,
            PspReference: "PSP-98765",
            Reason: "Authorized",
            Success: "true");

        var signature = _validator.CalculateSignature(item, TestHexKey);
        signature.Should().NotBeNullOrWhiteSpace();

        item.AdditionalData!["hmacSignature"] = signature;

        var isValid = _validator.Validate(item);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithTamperedAmount_ReturnsFalse()
    {
        var item = new AdyenNotificationRequestItem(
            AdditionalData: new Dictionary<string, string>(),
            Amount: new AdyenAmountDto("EUR", 1000),
            EventCode: "AUTHORISATION",
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "TestAccount",
            MerchantReference: "PAY-12345",
            OriginalReference: null,
            PspReference: "PSP-98765",
            Reason: "Authorized",
            Success: "true");

        var signature = _validator.CalculateSignature(item, TestHexKey);

        // Tamper amount
        var tamperedItem = item with
        {
            AdditionalData = new Dictionary<string, string> { ["hmacSignature"] = signature },
            Amount = new AdyenAmountDto("EUR", 9999)
        };

        var isValid = _validator.Validate(tamperedItem);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithTamperedEventCode_ReturnsFalse()
    {
        var item = new AdyenNotificationRequestItem(
            AdditionalData: new Dictionary<string, string>(),
            Amount: new AdyenAmountDto("EUR", 1000),
            EventCode: "AUTHORISATION",
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "TestAccount",
            MerchantReference: "PAY-12345",
            OriginalReference: null,
            PspReference: "PSP-98765",
            Reason: "Authorized",
            Success: "true");

        var signature = _validator.CalculateSignature(item, TestHexKey);

        var tamperedItem = item with
        {
            AdditionalData = new Dictionary<string, string> { ["hmacSignature"] = signature },
            EventCode = "CAPTURE"
        };

        var isValid = _validator.Validate(tamperedItem);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithSpecialCharactersRequiringEscape_SignsAndValidatesCorrectly()
    {
        var item = new AdyenNotificationRequestItem(
            AdditionalData: new Dictionary<string, string>(),
            Amount: new AdyenAmountDto("EUR", 2500),
            EventCode: "AUTHORISATION",
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "Test:Account\\WithSpecial",
            MerchantReference: "REF:123\\456",
            OriginalReference: "ORIG:99\\00",
            PspReference: "PSP:77\\88",
            Reason: "Special\\Reason:OK",
            Success: "true");

        var signature = _validator.CalculateSignature(item, TestHexKey);
        item.AdditionalData!["hmacSignature"] = signature;

        var isValid = _validator.Validate(item);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMissingSignature_ReturnsFalse()
    {
        var item = new AdyenNotificationRequestItem(
            AdditionalData: null,
            Amount: new AdyenAmountDto("EUR", 1000),
            EventCode: "AUTHORISATION",
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "TestAccount",
            MerchantReference: "PAY-12345",
            OriginalReference: null,
            PspReference: "PSP-98765",
            Reason: null,
            Success: "true");

        var isValid = _validator.Validate(item);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithWrongKey_ReturnsFalse()
    {
        var wrongKey = "0000000000000000000000000000000000000000000000000000000000000000";
        var item = new AdyenNotificationRequestItem(
            AdditionalData: new Dictionary<string, string>(),
            Amount: new AdyenAmountDto("EUR", 1000),
            EventCode: "AUTHORISATION",
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "TestAccount",
            MerchantReference: "PAY-12345",
            OriginalReference: null,
            PspReference: "PSP-98765",
            Reason: "Authorized",
            Success: "true");

        var signature = _validator.CalculateSignature(item, wrongKey);
        item.AdditionalData!["hmacSignature"] = signature;

        // Validating with default TestHexKey should fail
        var isValid = _validator.Validate(item);
        isValid.Should().BeFalse();
    }
}
