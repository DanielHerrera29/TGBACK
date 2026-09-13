using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using TransportesGutierrez.Api.Dtos;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/ordenes-escolta")]
public sealed class OrdenesEscoltaController : ControllerBase
{
    private readonly AppSessionService _sessions;
    private readonly SupabaseService _db;
    private readonly EmailSender _email;
    private readonly ILogger<OrdenesEscoltaController> _logger;

    public OrdenesEscoltaController(
        AppSessionService sessions,
        SupabaseService db,
        EmailSender email,
        ILogger<OrdenesEscoltaController> logger)
    {
        _sessions = sessions;
        _db = db;
        _email = email;
        _logger = logger;
    }

    [HttpPost("reservar")]
    public IActionResult Reservar([FromBody] CrearOrdenEscoltaDto dto)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Unauthorized();
        return StatusCode(StatusCodes.Status426UpgradeRequired, new {
            error = "Actualice la aplicación. La reserva de órdenes requiere identificación de solicitud para evitar duplicados."
        });
    }

    [HttpPost("{id}/enviar")]
    public async Task<IActionResult> Enviar(string id, [FromBody] EnviarOrdenEscoltaDto dto, CancellationToken cancellationToken)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(dto.PdfBase64)) return BadRequest(new { error = "El PDF es obligatorio." });

        var orden = await _db.ObtenerOrdenEscoltaAsync(id);
        if (orden is null || !string.Equals(orden.CreatedBy, session.UserId, StringComparison.OrdinalIgnoreCase))
            return NotFound();

        byte[] pdf;
        try { pdf = Convert.FromBase64String(dto.PdfBase64); }
        catch (FormatException) { return BadRequest(new { error = "El PDF no tiene un formato valido." }); }
        if (pdf.Length == 0 || pdf.Length > 12 * 1024 * 1024)
            return BadRequest(new { error = "El PDF es invalido o excede el limite permitido." });

        // Validate before acquiring the delivery lock: no request has reached the mail provider.
        try { _email.ValidarConfiguracion(); }
        catch (EmailDeliveryException) {
            return StatusCode(503, new { code = "CORREO_NO_CONFIGURADO", error = "Falta configurar Brevo en este backend. La orden está guardada; no se inició un nuevo envío." });
        }
        var entrega = await _db.ReclamarEntregaOrdenAsync(session.UserId, id, Convert.ToHexString(SHA256.HashData(pdf)));
        if (entrega == "ENVIADA") return Ok(new { consecutivo = orden.Consecutivo, yaEnviada = true });
        if (entrega != "RECLAMADA") return Conflict(new { error = "La entrega está en curso o pendiente de verificar. Administración debe consultar el proveedor antes de reenviar." });
        var correoIniciado = false;
        try
        {
            await _db.SubirPdfOrdenEscoltaAsync(orden, pdf);
            correoIniciado = true;
            await _email.EnviarOrdenEscoltaAsync(orden.Consecutivo, pdf, cancellationToken);
            await _db.FinalizarEntregaOrdenAsync(session.UserId, id, "ENVIADA");
            return Ok(new { consecutivo = orden.Consecutivo, destinatario = "transportegutierrezremesas@gmail.com" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar la orden de escolta {OrdenId}", id);
            if (ex is EmailDeliveryException { CanRetrySafely: true }) correoIniciado = false;
            var mensaje = correoIniciado
                ? "La orden se conserva. El resultado del correo debe verificarse con el proveedor antes de reenviar."
                : ex is EmailDeliveryException ? MensajeErrorCorreo(ex)
                : "No se pudo preparar el PDF. La orden se conserva y puede reintentarse.";
            try { await _db.FinalizarEntregaOrdenAsync(session.UserId, id, correoIniciado ? "POR_VERIFICAR" : "ERROR_PREVIO"); }
            catch (Exception saveError) { _logger.LogError(saveError, "No se pudo actualizar el control de entrega {OrdenId}", id); }
            await _db.RegistrarErrorEmailOrdenEscoltaAsync(id, mensaje);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = mensaje });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Unauthorized();
        var ordenes = await _db.GetOrdenesEscoltaAsync(session.UserId, session.Role == "admin");
        return Ok(ordenes);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> ObtenerPdf(string id)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Unauthorized();
        var orden = await _db.ObtenerOrdenEscoltaAsync(id);
        if (orden is null || (session.Role != "admin" && !string.Equals(orden.CreatedBy, session.UserId, StringComparison.OrdinalIgnoreCase)))
            return NotFound();
        if (string.IsNullOrWhiteSpace(orden.PdfPath))
            return NotFound(new { error = "Esta orden no tiene PDF almacenado." });
        var url = await _db.CrearUrlFirmadaPdfOrdenAsync(orden.PdfPath);
        return url is null ? StatusCode(StatusCodes.Status502BadGateway) : Ok(new { url });
    }

    private static bool EsValida(CrearOrdenEscoltaDto dto) =>
        dto.Fecha != default &&
        !string.IsNullOrWhiteSpace(dto.Empresa) &&
        !string.IsNullOrWhiteSpace(dto.PlacaCamabaja) &&
        dto.Viajes.Count > 0 &&
        dto.Viajes.All(v => !string.IsNullOrWhiteSpace(v.Maquina)
            && !string.IsNullOrWhiteSpace(v.Origen)
            && !string.IsNullOrWhiteSpace(v.Destino));

    private static string MensajeErrorCorreo(Exception exception)
    {
        if (exception is TimeoutException || exception.InnerException is TimeoutException)
            return "La orden fue guardada, pero Gmail no respondio a tiempo. Intente reenviarla en unos minutos.";

        if (exception is EmailDeliveryException delivery)
        {
            if (delivery.StatusCode is null)
                return "La orden fue guardada, pero falta configurar Brevo en el servidor.";

            return delivery.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                    "La orden fue guardada, pero Brevo rechazo la configuracion del remitente. Revise la clave API y verifique el correo remitente en Brevo.",
                System.Net.HttpStatusCode.BadRequest =>
                    "La orden está guardada. Brevo rechazó los datos del correo; administración debe revisar el remitente y el destinatario antes de reintentar.",
                System.Net.HttpStatusCode.TooManyRequests =>
                    "La orden está guardada. Brevo limitó temporalmente las solicitudes; espere antes de reintentar el envío.",
                _ => "La orden fue guardada, pero Brevo no pudo entregar el correo. Intente reenviarla."
            };
        }

        return "La orden fue guardada, pero no fue posible enviarla por correo. Intente reenviarla.";
    }
}
