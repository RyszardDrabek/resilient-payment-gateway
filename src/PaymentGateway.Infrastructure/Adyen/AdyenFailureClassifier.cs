using System.Net;

namespace PaymentGateway.Infrastructure.Adyen;

public static class AdyenFailureClassifier
{
    public static bool IsRetrySafeStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or (HttpStatusCode)429
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    public static bool IsRetrySafeException(Exception ex) =>
        ex is HttpRequestException
            or TimeoutException
            or TaskCanceledException;

    public static bool IsTerminalClientError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.UnprocessableEntity;

    public static bool IsAuthenticationError(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden;
}
