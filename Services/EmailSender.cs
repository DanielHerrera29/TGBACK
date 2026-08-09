using System.Net;
using System.Net.Mail;

namespace TransportesGutierrez.Api.Services;

public sealed class EmailSender
{
    private readonly IConfiguration _configuration;

    public EmailSender(IConfiguration configuration) => _configuration = configuration;

    public async Task EnviarOrdenEscoltaAsync(
        CredencialEmail credencial,
        long consecutivo,
        byte[] pdf,
        CancellationToken cancellationToken = default)
    {
        var destinatario = _configuration["OrdenEscolta:Destinatario"]
            ?? "transportegutierrezremesas@gmail.com";
        var host = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
        var puerto = int.TryParse(_configuration["Smtp:Port"], out var value) ? value : 587;
        var nombreArchivo = $"orden_escolta_{consecutivo:D5}.pdf";

        using var mensaje = new MailMessage
        {
            From = new MailAddress(credencial.CorreoEmail, "Transportes Especiales Gutierrez"),
            Subject = $"Orden de escolta No. {consecutivo:D5}",
            Body = "Se adjunta la orden de escolta generada por CargoDespacho.",
            IsBodyHtml = false
        };
        mensaje.To.Add(destinatario);

        await using var contenidoPdf = new MemoryStream(pdf, writable: false);
        mensaje.Attachments.Add(new Attachment(contenidoPdf, nombreArchivo, "application/pdf"));

        using var smtp = new SmtpClient(host, puerto)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Timeout = 40_000,
            Credentials = new NetworkCredential(credencial.CorreoEmail, credencial.ContrasenaApp)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(mensaje)
            .WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);
    }
}
