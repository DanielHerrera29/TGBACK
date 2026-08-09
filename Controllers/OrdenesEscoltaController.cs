using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> Reservar([FromBody] CrearOrdenEscoltaDto dto)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Unauthorized();
        if (!EsValida(dto)) return BadRequest(new { error = "Complete los datos de la orden y de cada viaje." });

        var creada = await _db.CrearOrdenEscoltaAsync(session.UserId, dto);
        return Ok(new { id = creada.Id, consecutivo = creada.Consecutivo });
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

        var credencial = await _db.ObtenerCredencialEmailUsuarioAsync(session.UserId);
        if (credencial is null)
            return Conflict(new { error = "Configure el correo remitente y la contrasena de aplicacion antes de generar ordenes." });

        try
        {
            await _db.SubirPdfOrdenEscoltaAsync(orden, pdf);
            await _email.EnviarOrdenEscoltaAsync(credencial, orden.Consecutivo, pdf, cancellationToken);
            await _db.MarcarOrdenEscoltaEnviadaAsync(id);
            return Ok(new { consecutivo = orden.Consecutivo, destinatario = "transportegutierrezremesas@gmail.com" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar la orden de escolta {OrdenId}", id);
            await _db.RegistrarErrorEmailOrdenEscoltaAsync(id, "No fue posible enviar el correo.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "La orden fue guardada, pero no fue posible enviarla por correo." });
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
}
