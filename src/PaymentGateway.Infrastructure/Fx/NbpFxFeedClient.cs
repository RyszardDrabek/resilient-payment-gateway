using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Fx.Ports;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Infrastructure.Fx;

public sealed class NbpFxFeedClient(
    HttpClient httpClient,
    IOptions<FxRateRefreshOptions> options,
    ILogger<NbpFxFeedClient> logger) : IExternalFxFeedClient
{
    private readonly FxRateRefreshOptions _options = options.Value;

    public async Task<IReadOnlyDictionary<string, decimal>?> FetchRatesToUsdAsync(CancellationToken ct = default)
    {
        try
        {
            var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 10);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var tables = await httpClient.GetFromJsonAsync<NbpTableDto[]>(_options.NbpApiUrl, cts.Token);
            if (tables is null || tables.Length == 0 || tables[0].Rates is null)
            {
                logger.LogWarning("NBP API returned empty exchange rate table.");
                return null;
            }

            var ratesList = tables[0].Rates;
            if (ratesList is null)
            {
                return null;
            }

            var midRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in ratesList)
            {
                if (!string.IsNullOrWhiteSpace(r.Code) && r.Mid > 0)
                {
                    midRates[r.Code.Trim().ToUpperInvariant()] = r.Mid;
                }
            }

            // PLN mid against PLN is 1.0
            midRates["PLN"] = 1.0m;

            if (!midRates.TryGetValue("USD", out var usdMidInPln) || usdMidInPln <= 0)
            {
                logger.LogWarning("USD rate not found in NBP feed.");
                return null;
            }

            // In NBP Table A, Mid is expressed in PLN per 1 unit of foreign currency:
            // 1 EUR = midRates["EUR"] PLN
            // 1 USD = usdMidInPln PLN
            // Rate to USD = Mid / usdMidInPln
            var ratesToUsd = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 1.0000m,
                ["USDC"] = 1.0000m,
                ["USDT"] = 1.0000m,
                ["PLN"] = Math.Round(1.0m / usdMidInPln, 6, MidpointRounding.AwayFromZero)
            };

            foreach (var (code, midInPln) in midRates)
            {
                if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(code, "PLN", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ratesToUsd[code] = Math.Round(midInPln / usdMidInPln, 6, MidpointRounding.AwayFromZero);
            }

            logger.LogInformation("Successfully fetched and normalized {Count} FX rates from NBP API (USD/PLN: {UsdPln}).",
                ratesToUsd.Count, usdMidInPln);

            return ratesToUsd;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch live FX rates from NBP API. Fallback will be used.");
            return null;
        }
    }

    private sealed record NbpTableDto(
        [property: JsonPropertyName("table")] string? Table,
        [property: JsonPropertyName("no")] string? No,
        [property: JsonPropertyName("effectiveDate")] string? EffectiveDate,
        [property: JsonPropertyName("rates")] List<NbpRateDto>? Rates);

    private sealed record NbpRateDto(
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("mid")] decimal Mid);
}
