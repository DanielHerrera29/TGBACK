namespace TransportesGutierrez.Api.Services;

public sealed class EmailDeliveryException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public bool RequestStarted { get; }

    public EmailDeliveryException(System.Net.HttpStatusCode? statusCode, string message, bool requestStarted = true)
        : base(message) { StatusCode = statusCode; RequestStarted = requestStarted; }
}
