using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

public sealed class PaymentRateLimitingUnitTests
{
    [Fact]
    public async Task HandleRateLimitRejectionAsync_Writes429_WithRFC7807ProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();
        context.TraceIdentifier = "rate-limit-trace-999";

        await PaymentRateLimitingExtensions.HandleRateLimitRejectionAsync(context);

        context.Response.StatusCode.Should().Be(429);
        context.Response.ContentType.Should().Be("application/problem+json");
        context.Response.Headers["X-Correlation-Id"].ToString().Should().Be("rate-limit-trace-999");

        context.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
        using var reader = new System.IO.StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.RateLimit);
        doc.GetProperty("status").GetInt32().Should().Be(429);
        doc.GetProperty("title").GetString().Should().Be("Too Many Requests");
        doc.GetProperty("correlationId").GetString().Should().Be("rate-limit-trace-999");
        doc.GetProperty("detail").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ResolveCallerIdentity_PrefersSubClaim()
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim("sub", "merchant-partner-42"),
            new Claim(ClaimTypes.NameIdentifier, "fallback-user")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        var identity = PaymentRateLimitingExtensions.ResolveCallerIdentity(context);

        identity.Should().Be("sub:merchant-partner-42");
    }

    [Fact]
    public void ResolveCallerIdentity_FallsBackToIp_WhenNoSub()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        var identity = PaymentRateLimitingExtensions.ResolveCallerIdentity(context);

        identity.Should().Be("ip:10.0.0.5");
    }

    [Fact]
    public void ResolveCallerIdentity_FallsBackToAnonymous_WhenNoIpOrSub()
    {
        var context = new DefaultHttpContext();

        var identity = PaymentRateLimitingExtensions.ResolveCallerIdentity(context);

        identity.Should().Be("anonymous");
    }
}
