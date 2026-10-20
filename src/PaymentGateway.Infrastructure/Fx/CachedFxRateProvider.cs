using System.Collections.Concurrent;
using PaymentGateway.Application.Fx.Ports;

namespace PaymentGateway.Infrastructure.Fx;

public sealed class CachedFxRateProvider : IFxRateProvider
{
    // Rates normalized against USD: 1 unit of Currency = X units of USD
    private readonly ConcurrentDictionary<string, decimal> _ratesToUsd = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1.0000m,
        ["USDC"] = 1.0000m,
        ["USDT"] = 1.0000m,
        ["EUR"] = 1.0850m,
        ["GBP"] = 1.2800m,
        ["PLN"] = 0.2520m, // 1 PLN = ~0.252 USD (USD/PLN ~ 3.968)
        ["CHF"] = 1.1300m
    };

    public Task<decimal> GetSpotRateAsync(string baseCurrency, string targetCurrency, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCurrency);

        var baseCode = baseCurrency.Trim().ToUpperInvariant();
        var targetCode = targetCurrency.Trim().ToUpperInvariant();

        if (string.Equals(baseCode, targetCode, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(1.0m);
        }

        if (!_ratesToUsd.TryGetValue(baseCode, out var baseToUsd))
        {
            throw new NotSupportedException($"Unsupported base currency for FX quoting: '{baseCode}'.");
        }

        if (!_ratesToUsd.TryGetValue(targetCode, out var targetToUsd))
        {
            throw new NotSupportedException($"Unsupported target currency for FX quoting: '{targetCode}'.");
        }

        // 1 Base = baseToUsd (in USD)
        // 1 Target = targetToUsd (in USD)
        // Rate (Base -> Target) = baseToUsd / targetToUsd
        var rate = Math.Round(baseToUsd / targetToUsd, 6, MidpointRounding.AwayFromZero);
        return Task.FromResult(rate);
    }

    public Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(string baseCurrency, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        var baseCode = baseCurrency.Trim().ToUpperInvariant();

        if (!_ratesToUsd.TryGetValue(baseCode, out var baseToUsd))
        {
            throw new NotSupportedException($"Unsupported base currency: '{baseCode}'.");
        }

        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (currency, targetToUsd) in _ratesToUsd)
        {
            if (string.Equals(currency, baseCode, StringComparison.OrdinalIgnoreCase))
            {
                result[currency] = 1.0m;
            }
            else
            {
                result[currency] = Math.Round(baseToUsd / targetToUsd, 6, MidpointRounding.AwayFromZero);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, decimal>>(result);
    }

    /// <summary>
    /// Allows setting or updating spot rates dynamically (e.g. for testing or external rate ingestion).
    /// </summary>
    public void SetRateToUsd(string currency, decimal rateToUsd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateToUsd);

        _ratesToUsd[currency.Trim().ToUpperInvariant()] = rateToUsd;
    }
}
