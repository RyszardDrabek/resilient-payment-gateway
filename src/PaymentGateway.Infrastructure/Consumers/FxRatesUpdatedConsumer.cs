using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Fx;

namespace PaymentGateway.Infrastructure.Consumers;

public sealed class FxRatesUpdatedConsumer(
    CachedFxRateProvider rateProvider,
    ILogger<FxRatesUpdatedConsumer> logger) : IConsumer<FxRatesRefreshedEvent>
{
    public Task Consume(ConsumeContext<FxRatesRefreshedEvent> context) =>
        ProcessEventAsync(context.Message);

    public Task ProcessEventAsync(FxRatesRefreshedEvent message)
    {
        logger.LogInformation("Received FxRatesRefreshedEvent with {Count} rates refreshed at {RefreshedAt}.",
            message.RatesToUsd.Count, message.RefreshedAt);

        foreach (var (currency, rateToUsd) in message.RatesToUsd)
        {
            rateProvider.SetRateToUsd(currency, rateToUsd);
        }

        return Task.CompletedTask;
    }
}
