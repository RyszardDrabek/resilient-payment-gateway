using System.Diagnostics;
using MediatR;

namespace PaymentGateway.Application.Common.Behaviors;

public sealed class TracingPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        using var activity = PaymentTracing.ActivitySource.StartActivity(
            $"MediatR {requestName}",
            ActivityKind.Internal);

        activity?.SetTag("mediatr.request", requestName);

        return await next();
    }
}
