using System.Diagnostics;

namespace PaymentGateway.Application.Common;

public static class PaymentTracing
{
    public const string ActivitySourceName = "PaymentGateway";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}
