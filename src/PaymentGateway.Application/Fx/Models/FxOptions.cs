namespace PaymentGateway.Application.Fx.Models;

public sealed class FxOptions
{
    public const string SectionName = "Fx";

    public int DefaultSpreadBps { get; set; } = 50; // 0.50%
    public int DefaultTtlMinutes { get; set; } = 15; // 15-minute rate lock
    public int MaxTtlMinutes { get; set; } = 60;
}
