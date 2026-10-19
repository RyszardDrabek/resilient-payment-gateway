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
        activity?.SetTag("mediatr.response_type", typeof(TResponse).Name);

        try
        {
            var response = await next();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message }
            }));
            throw;
        }
    }
}
