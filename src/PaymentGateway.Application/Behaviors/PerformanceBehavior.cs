using System.Diagnostics;
using MediatR;
using PaymentGateway.Application.Metrics;

namespace PaymentGateway.Application.Behaviors;

/// <summary>
/// Pipeline behavior measuring execution duration of application requests and recording metrics.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var status = "success";

        try
        {
            return await next();
        }
        catch
        {
            status = "failed";
            throw;
        }
        finally
        {
            var elapsedSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            PaymentMetrics.RecordDuration(elapsedSeconds, typeof(TRequest).Name, status);
        }
    }
}
