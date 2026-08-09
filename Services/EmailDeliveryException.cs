namespace TransportesGutierrez.Api.Services;

public sealed class EmailDeliveryException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }

    public EmailDeliveryException(System.Net.HttpStatusCode? statusCode, string message)
        : base(message) => StatusCode = statusCode;
}
