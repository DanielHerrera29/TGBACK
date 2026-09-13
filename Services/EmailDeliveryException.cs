namespace TransportesGutierrez.Api.Services;

public sealed class EmailDeliveryException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public bool RequestStarted { get; }
    // An explicit rejection is different from a timeout or an ambiguous 5xx.
    public bool CanRetrySafely => !RequestStarted || StatusCode is
        System.Net.HttpStatusCode.BadRequest or
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden or
        System.Net.HttpStatusCode.TooManyRequests;

    public EmailDeliveryException(System.Net.HttpStatusCode? statusCode, string message, bool requestStarted = true)
        : base(message) { StatusCode = statusCode; RequestStarted = requestStarted; }
}
