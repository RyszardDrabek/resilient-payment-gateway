using MediatR;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Commands;

public enum DispositionOutcome
{
    Success,
    NotFound,
    AlreadyDispositioned,
    InvalidDisposition
}

public sealed record DispositionRiskFlagResult(
    DispositionOutcome Outcome,
    RiskFlagDto? Flag = null,
    string? ErrorMessage = null)
{
    public static DispositionRiskFlagResult Succeeded(RiskFlagDto flag) =>
        new(DispositionOutcome.Success, Flag: flag);

    public static DispositionRiskFlagResult NotFound(string flagId) =>
        new(DispositionOutcome.NotFound, ErrorMessage: $"No risk flag found matching identifier '{flagId}'.");

    public static DispositionRiskFlagResult AlreadyDispositioned(string flagId, string? existingDisposition = null) =>
        new(DispositionOutcome.AlreadyDispositioned, ErrorMessage: $"Risk flag '{flagId}' has already been dispositioned{(existingDisposition is not null ? $" as '{existingDisposition}'" : "")}.");

    public static DispositionRiskFlagResult InvalidDisposition(string disposition) =>
        new(DispositionOutcome.InvalidDisposition, ErrorMessage: $"Unsupported disposition value '{disposition}'. Supported values: confirmed, false_positive, escalated.");
}

public sealed record DispositionRiskFlagCommand(string FlagId, string Disposition) : IRequest<DispositionRiskFlagResult>;

public sealed class DispositionRiskFlagCommandHandler(IRiskFlagRepository repository)
    : IRequestHandler<DispositionRiskFlagCommand, DispositionRiskFlagResult>
{
    public async Task<DispositionRiskFlagResult> Handle(DispositionRiskFlagCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Disposition))
        {
            return DispositionRiskFlagResult.InvalidDisposition(request.Disposition ?? string.Empty);
        }

        var normalized = request.Disposition.Trim().ToLowerInvariant();
        if (!RiskFlag.SupportedDispositions.Contains(normalized))
        {
            return DispositionRiskFlagResult.InvalidDisposition(request.Disposition);
        }

        var flag = await repository.GetByIdAsync(request.FlagId, ct);
        if (flag is null)
        {
            return DispositionRiskFlagResult.NotFound(request.FlagId);
        }

        if (flag.IsDispositioned)
        {
            return DispositionRiskFlagResult.AlreadyDispositioned(flag.Id, flag.Disposition);
        }

        try
        {
            flag.ApplyDisposition(normalized, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return DispositionRiskFlagResult.AlreadyDispositioned(flag.Id, flag.Disposition);
        }
        catch (ArgumentException)
        {
            return DispositionRiskFlagResult.InvalidDisposition(request.Disposition);
        }

        await repository.SaveAsync(flag, ct);
        return DispositionRiskFlagResult.Succeeded(RiskFlagDto.FromDomain(flag));
    }
}
