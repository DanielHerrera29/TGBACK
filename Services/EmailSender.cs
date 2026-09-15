using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TransportesGutierrez.Api.Services;

public sealed class EmailSender
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _http;

    public EmailSender(IConfiguration configuration, HttpClient http)
    {
        _configuration = configuration;
        _http = http;
    }

    public void ValidarConfiguracion()
    {
        if (string.IsNullOrWhiteSpace(_configuration["Brevo:ApiKey"]) || string.IsNullOrWhiteSpace(_configuration["Brevo:SenderEmail"]))
            throw new EmailDeliveryException(null, "Falta configurar Brevo en este backend. La orden está guardada; el correo no se inició.", requestStarted: false);
    }
    public async Task EnviarOrdenEscoltaAsync(
        long consecutivo,
        byte[] pdf,
        CancellationToken cancellationToken = default,
        string? codigoOrden = null)
    {
        ValidarConfiguracion();
        var destinatario = _configuration["OrdenEscolta:Destinatario"]
            ?? "transportegutierrezremesas@gmail.com";
        var apiKey = _configuration["Brevo:ApiKey"];
        var remitente = _configuration["Brevo:SenderEmail"];
        var nombreRemitente = _configuration["Brevo:SenderName"] ?? "Transportes Especiales Gutierrez";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(remitente))
            throw new EmailDeliveryException(null, "Falta configurar Brevo.", requestStarted: false);

        var numeroVisible = codigoOrden ?? consecutivo.ToString("D5");
        var nombreArchivo = $"orden_escolta_{numeroVisible}.pdf";
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            sender = new { email = remitente, name = nombreRemitente },
            to = new[] { new { email = destinatario, name = "Ordenes de escolta" } },
            subject = $"Orden de escolta No. {numeroVisible}",
            textContent = "Se adjunta la orden de escolta generada por CargoDespacho.",
            attachment = new[] { new { content = Convert.ToBase64String(pdf), name = nombreArchivo } },
            tags = new[] { "orden-escolta" }
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new EmailDeliveryException(response.StatusCode, "Brevo rechazo la solicitud de correo.");
    }
}
