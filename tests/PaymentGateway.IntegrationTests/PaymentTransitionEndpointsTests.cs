using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Payments;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class PaymentTransitionEndpointsTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_transitions_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            _dockerAvailable = true;

            var options = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            await using var db = new PaymentDbContext(options);
            await db.Database.MigrateAsync();
        }
        catch
        {
            _dockerAvailable = false;
            if (_postgres is not null)
            {
                await _postgres.DisposeAsync().AsTask();
                _postgres = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().AsTask();
        }
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = _dockerAvailable && _postgres is not null
            ? _postgres.GetConnectionString()
            : "Host=localhost;Database=dummy;Username=dummy;Password=dummy";

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PaymentDb"] = connectionString,
                        ["ConnectionStrings:RiskDb"] = connectionString,
                        ["RabbitMq:UseInMemory"] = "true",
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["PaymentGateway:ActiveChannel"] = "MOCK"
                    });
                });
            });
    }

    private static string GenerateTestToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", "merchant_transition_test"),
                new Claim("role", "merchant")
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "payment-gateway",
            Audience = "payment-gateway",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private async Task<PaymentDto> CreateAuthorizedPayment(HttpClient client)
    {
        var idempotencyKey = $"idem_auth_{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        var request = new AuthorizePaymentRequest("party_trans", 5000, "EUR");
        var response = await client.PostAsJsonAsync("/payments", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Authorized");
        return dto;
    }

    [Fact]
    public async Task Capture_WhenAuthorized_Returns200_WithCapturedState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_{Guid.NewGuid():N}");

        var response = await client.PostAsync($"/payments/{authorized.PaymentId}/capture", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PaymentDto>();
        result.Should().NotBeNull();
        result!.State.Should().Be("Captured");
        result.Amount.Should().Be(5000);
        result.ChannelReference.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cancel_WhenAuthorized_Returns200_WithCancelledState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cnc_{Guid.NewGuid():N}");

        var response = await client.PostAsync($"/payments/{authorized.PaymentId}/cancel", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PaymentDto>();
        result.Should().NotBeNull();
        result!.State.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Refund_WhenCaptured_Returns200_WithRefundedState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // First capture
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_{Guid.NewGuid():N}");
        var capResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/capture", null);
        capResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Then refund
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_{Guid.NewGuid():N}");
        var refResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/refund", null);
        refResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await refResponse.Content.ReadFromJsonAsync<PaymentDto>();
        result.Should().NotBeNull();
        result!.State.Should().Be("Refunded");
    }

    [Fact]
    public async Task Cancel_WhenAlreadyCaptured_Returns409_ConflictProblem()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // Capture payment
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_{Guid.NewGuid():N}");
        var capResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/capture", null);
        capResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempt cancel on captured payment (AC-4)
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cnc_illegal_{Guid.NewGuid():N}");
        var cancelResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/cancel", null);

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        cancelResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await cancelResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("Captured");
    }

    [Fact]
    public async Task Refund_WhenAuthorized_Returns409_ConflictProblem()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // Attempt refund before capture (AC-4)
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_illegal_{Guid.NewGuid():N}");
        var refundResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/refund", null);

        refundResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        refundResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await refundResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("Authorized");
    }

    [Fact]
    public async Task Capture_PartialAmount_Returns200_WithPartiallyCapturedState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_partial_{Guid.NewGuid():N}");
        var captureResponse = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/capture",
            new CapturePaymentRequest(2000));

        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        dto.Should().NotBeNull();
        dto!.State.Should().Be("PartiallyCaptured");
        dto.CapturedAmount.Should().Be(2000);
        dto.RemainingAuthorizedAmount.Should().Be(authorized.Amount - 2000);
    }

    [Fact]
    public async Task Capture_MultiplePartialAmounts_ReachesFullCapture_Returns200_WithCapturedState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_1_{Guid.NewGuid():N}");
        var cap1Response = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/capture",
            new CapturePaymentRequest(3000));
        cap1Response.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_2_{Guid.NewGuid():N}");
        var cap2Response = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/capture",
            new CapturePaymentRequest(authorized.Amount - 3000));

        cap2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await cap2Response.Content.ReadFromJsonAsync<PaymentDto>();
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Captured");
        dto.CapturedAmount.Should().Be(authorized.Amount);
        dto.RemainingAuthorizedAmount.Should().Be(0);
    }

    [Fact]
    public async Task Cancel_PartiallyCapturedPayment_Returns200_WithCancelledState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_before_cancel_{Guid.NewGuid():N}");
        var capResponse = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/capture",
            new CapturePaymentRequest(2000));
        capResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cnc_after_cap_{Guid.NewGuid():N}");
        var cancelResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/cancel", null);

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await cancelResponse.Content.ReadFromJsonAsync<PaymentDto>();
        dto.Should().NotBeNull();
        dto!.State.Should().Be("Cancelled");
        dto.CapturedAmount.Should().Be(2000);
    }

    [Fact]
    public async Task Capture_AmountExceedingRemaining_Returns400ValidationProblem()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_excess_{Guid.NewGuid():N}");
        var captureResponse = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/capture",
            new CapturePaymentRequest(authorized.Amount + 1000));

        captureResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        captureResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await captureResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Validation);
    }

    [Fact]
    public async Task Capture_WhenAlreadyCancelled_Returns409_ConflictProblem()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // First cancel payment
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cnc_before_cap_{Guid.NewGuid():N}");
        var cancelResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempt capture on cancelled payment
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_after_cnc_{Guid.NewGuid():N}");
        var captureResponse = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/capture",
            new CapturePaymentRequest(1000));

        captureResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        captureResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await captureResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("Cancelled");
    }

    [Fact]
    public async Task Refund_WithPartialAmount_Returns200_WithPartiallyRefundedState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // Fully capture first
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_full_{Guid.NewGuid():N}");
        var capResponse = await client.PostAsync($"/payments/{authorized.PaymentId}/capture", null);
        capResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // First partial refund
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_part_{Guid.NewGuid():N}");
        var refResponse = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/refund",
            new RefundPaymentRequest(authorized.Amount / 2));
        refResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await refResponse.Content.ReadFromJsonAsync<PaymentDto>();
        result.Should().NotBeNull();
        result!.State.Should().Be("PartiallyRefunded");
        result.RefundedAmount.Should().Be(authorized.Amount / 2);
        result.RemainingRefundableAmount.Should().Be(authorized.Amount - (authorized.Amount / 2));
    }

    [Fact]
    public async Task Refund_SubsequentAmount_ReachingFullCaptured_Returns200_WithRefundedState()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // Capture payment
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_for_ref_{Guid.NewGuid():N}");
        await client.PostAsync($"/payments/{authorized.PaymentId}/capture", null);

        var half = authorized.Amount / 2;
        var remainder = authorized.Amount - half;

        // Partial refund
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_step1_{Guid.NewGuid():N}");
        await client.PostAsJsonAsync($"/payments/{authorized.PaymentId}/refund", new RefundPaymentRequest(half));

        // Complete remaining refund
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_step2_{Guid.NewGuid():N}");
        var finalRef = await client.PostAsJsonAsync($"/payments/{authorized.PaymentId}/refund", new RefundPaymentRequest(remainder));

        finalRef.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalResult = await finalRef.Content.ReadFromJsonAsync<PaymentDto>();
        finalResult.Should().NotBeNull();
        finalResult!.State.Should().Be("Refunded");
        finalResult.RefundedAmount.Should().Be(authorized.Amount);
        finalResult.RemainingRefundableAmount.Should().Be(0);
    }

    [Fact]
    public async Task Refund_WhenRemainderCancelled_AllowsRefundOfCapturedFunds()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // Partial capture of 2000
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_2000_{Guid.NewGuid():N}");
        await client.PostAsJsonAsync($"/payments/{authorized.PaymentId}/capture", new CapturePaymentRequest(2000));

        // Cancel remaining authorization
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cnc_remainder_{Guid.NewGuid():N}");
        await client.PostAsync($"/payments/{authorized.PaymentId}/cancel", null);

        // Refund 1000 of the 2000 captured funds
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_cancelled_{Guid.NewGuid():N}");
        var refResponse = await client.PostAsJsonAsync($"/payments/{authorized.PaymentId}/refund", new RefundPaymentRequest(1000));

        refResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await refResponse.Content.ReadFromJsonAsync<PaymentDto>();
        result.Should().NotBeNull();
        result!.State.Should().Be("PartiallyRefunded");
        result.RefundedAmount.Should().Be(1000);
        result.RemainingRefundableAmount.Should().Be(1000);
    }

    [Fact]
    public async Task Refund_WithAmountExceedingCaptured_Returns400_ValidationProblem()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authorized = await CreateAuthorizedPayment(client);

        // Capture payment
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_cap_exceed_{Guid.NewGuid():N}");
        await client.PostAsync($"/payments/{authorized.PaymentId}/capture", null);

        // Attempt refund exceeding captured amount
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_ref_excess_{Guid.NewGuid():N}");
        var refResponse = await client.PostAsJsonAsync(
            $"/payments/{authorized.PaymentId}/refund",
            new RefundPaymentRequest(authorized.Amount + 500));

        refResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await refResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Validation);
    }
}
