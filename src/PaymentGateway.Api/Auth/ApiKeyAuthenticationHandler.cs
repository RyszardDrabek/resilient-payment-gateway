using System.Security.Claims;
using System.Text.Encodings.Web;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.ApiKeys;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Api.Auth;

public static class ApiKeyDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISender sender)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var apiKeyHeader) ||
            string.IsNullOrWhiteSpace(apiKeyHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var rawKey = apiKeyHeader.ToString().Trim();
        var result = await sender.Send(new ValidateMerchantApiKeyQuery(rawKey), Context.RequestAborted);

        if (!result.IsValid || string.IsNullOrWhiteSpace(result.MerchantId))
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.MerchantId),
            new("merchant_id", result.MerchantId),
            new("merchantId", result.MerchantId),
            new(ClaimTypes.Role, "merchant"),
            new("role", "merchant"),
            new("auth_method", "api_key")
        };

        if (result.Scopes is not null)
        {
            foreach (var scope in result.Scopes)
            {
                claims.Add(new("scope", scope));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.NameIdentifier, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        var correlationId = Context.TraceIdentifier;
        if (!Response.Headers.ContainsKey("X-Correlation-Id"))
        {
            Response.Headers["X-Correlation-Id"] = correlationId;
        }

        var problem = new PaymentProblemDetails(
            Type: PaymentProblemTypes.Unauthorized,
            Title: "Unauthorized",
            Status: StatusCodes.Status401Unauthorized,
            Detail: "Authentication credentials (API key or Bearer token) are missing, invalid, or revoked.",
            CorrelationId: correlationId);

        await Response.WriteAsJsonAsync(
            problem,
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: Context.RequestAborted);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/problem+json";
        var correlationId = Context.TraceIdentifier;
        if (!Response.Headers.ContainsKey("X-Correlation-Id"))
        {
            Response.Headers["X-Correlation-Id"] = correlationId;
        }

        var problem = new PaymentProblemDetails(
            Type: PaymentProblemTypes.Forbidden,
            Title: "Forbidden",
            Status: StatusCodes.Status403Forbidden,
            Detail: "The caller does not have the required permissions or scopes to access this resource.",
            CorrelationId: correlationId);

        await Response.WriteAsJsonAsync(
            problem,
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: Context.RequestAborted);
    }
}
