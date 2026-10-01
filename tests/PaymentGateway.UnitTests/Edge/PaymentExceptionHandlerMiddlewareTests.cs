using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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

    private static async Task<(int StatusCode, string Body, string ContentType)> RunMiddleware(
        HttpContext context,
        RequestDelegate next)
    {
        var middleware = new PaymentExceptionHandlerMiddleware(next);
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
        using var reader = new System.IO.StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body, context.Response.ContentType ?? "");
    }

    [Fact]
    public async Task NoException_PassesThrough()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => Task.CompletedTask);

        var (statusCode, body, _) = await RunMiddleware(context, next);

        statusCode.Should().Be(200);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task IdempotencyKeyMissingException_Returns400_ValidationProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new IdempotencyKeyMissingException());

        var (statusCode, body, contentType) = await RunMiddleware(context, next);

        statusCode.Should().Be(400);
        contentType.Should().Contain("application/problem+json");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Validation);
        doc.GetProperty("correlationId").GetString().Should().Be("test-trace-id");
    }

    [Fact]
    public async Task IdempotencyConflictException_Returns409_ConflictProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new IdempotencyConflictException());

        var (statusCode, body, contentType) = await RunMiddleware(context, next);

        statusCode.Should().Be(409);
        contentType.Should().Contain("application/problem+json");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
    }

    [Fact]
    public async Task IdempotencyInFlightException_Returns409_ConflictProblem()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new IdempotencyInFlightException());

        var (statusCode, body, contentType) = await RunMiddleware(context, next);

        statusCode.Should().Be(409);
        contentType.Should().Contain("application/problem+json");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Conflict);
    }

    [Fact]
    public async Task UnhandledException_Returns500_ServerErrorProblem_WithoutStackTrace()
    {
        var context = MakeContext();
        var next = new RequestDelegate(_ => throw new InvalidOperationException("Internal details that must not leak"));

        var (statusCode, body, contentType) = await RunMiddleware(context, next);

        statusCode.Should().Be(500);
        contentType.Should().Contain("application/problem+json");
        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        doc.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.ServerError);
        // Must NOT expose the original exception message or stack trace
        body.Should().NotContain("Internal details that must not leak");
        body.Should().NotContain("InvalidOperationException");
        body.Should().NotContain("at PaymentGateway");
    }
}
