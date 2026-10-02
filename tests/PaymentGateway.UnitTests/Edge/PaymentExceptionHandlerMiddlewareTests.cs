using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Edge.Middleware;
using PaymentGateway.Edge.Problems;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

public sealed class PaymentExceptionHandlerMiddlewareTests
{
    private static DefaultHttpContext MakeContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        ctx.TraceIdentifier = "test-trace-id";
        return ctx;
    }

    private static async Task<(int StatusCode, string Body, string ContentType, IHeaderDictionary Headers)> RunMiddleware(
        HttpContext context,
        RequestDelegate next)
    {
        var middleware = new PaymentExceptionHandlerMiddleware(
            next,
            NullLogger<PaymentExceptionHandlerMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
        using var reader = new System.IO.StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body, context.Response.ContentType ?? "", context.Response.Headers);
    }

    [Fact]
    public async Task NoException_PassesThrough()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => Task.CompletedTask);

        var (statusCode, body, _, _) = await RunMiddleware(context, next);

        statusCode.Should().Be(200);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task IdempotencyKeyMissingException_Returns400_ValidationProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new IdempotencyKeyMissingException());

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(400);
        contentType.Should().Contain("application/problem+json");
        headers["X-Correlation-Id"].ToString().Should().Be("test-trace-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Validation);
        doc.GetProperty("correlationId").GetString().Should().Be("test-trace-id");
    }

    [Fact]
    public async Task IdempotencyConflictException_Returns409_ConflictProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new IdempotencyConflictException());

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(409);
        contentType.Should().Contain("application/problem+json");
        headers["X-Correlation-Id"].ToString().Should().Be("test-trace-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
    }

    [Fact]
    public async Task IdempotencyInFlightException_Returns409_ConflictProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new IdempotencyInFlightException());

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(409);
        contentType.Should().Contain("application/problem+json");
        headers["X-Correlation-Id"].ToString().Should().Be("test-trace-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
    }

    [Fact]
    public async Task PaymentInvalidStateException_Returns409_ConflictProblem_NamingCurrentState()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new PaymentInvalidStateException(PaymentState.Captured, "Cancel"));

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(409);
        contentType.Should().Contain("application/problem+json");
        headers["X-Correlation-Id"].ToString().Should().Be("test-trace-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
        doc.GetProperty("detail").GetString().Should().Contain("Captured");
        doc.GetProperty("detail").GetString().Should().Contain("Cancel");
    }

    [Fact]
    public async Task PaymentNotFoundException_Returns404_NotFoundProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new PaymentNotFoundException("pay_abc123"));

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(404);
        contentType.Should().Contain("application/problem+json");
        headers["X-Correlation-Id"].ToString().Should().Be("test-trace-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.NotFound);
        doc.GetProperty("detail").GetString().Should().Contain("pay_abc123");
    }

    [Fact]
    public async Task PaymentOperationFailedException_Returns422_Problem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new PaymentOperationFailedException("Capture", "Channel unreachable"));

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(422);
        contentType.Should().Contain("application/problem+json");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
        doc.GetProperty("detail").GetString().Should().Contain("Channel unreachable");
    }

    [Fact]
    public async Task UnhandledException_Returns500_ServerErrorProblem_WithoutStackTrace()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new InvalidOperationException("Internal details that must not leak"));

        var (statusCode, body, contentType, headers) = await RunMiddleware(context, next);

        statusCode.Should().Be(500);
        contentType.Should().Contain("application/problem+json");
        headers["X-Correlation-Id"].ToString().Should().Be("test-trace-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.ServerError);
        // Must NOT expose the original exception message or stack trace
        body.Should().NotContain("Internal details that must not leak");
        body.Should().NotContain("InvalidOperationException");
        body.Should().NotContain("at PaymentGateway");
    }
}
