using Microsoft.AspNetCore.Mvc;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class UsuariosController : ControllerBase
{
    private readonly SupabaseService _db;
    private readonly AppSessionService _sessions;
    public UsuariosController(SupabaseService db, AppSessionService sessions) => (_db, _sessions) = (db, sessions);

    [HttpPost("{id}/correo")]
    public async Task<IActionResult> GuardarCorreo(string id, [FromBody] CredencialCorreoDto dto)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session == null) return Unauthorized();
        if (!string.Equals(session.Role, "admin", StringComparison.OrdinalIgnoreCase)) return Forbid();
        if (!Guid.TryParse(id, out _)) return BadRequest(new { error = "Usuario inválido" });
        if (string.IsNullOrWhiteSpace(dto.CorreoEmail) || string.IsNullOrWhiteSpace(dto.ContrasenaApp))
            return BadRequest(new { error = "Correo y contraseña de aplicación son obligatorios" });
        await _db.GuardarCredencialEmailUsuarioAsync(id, dto.CorreoEmail, dto.ContrasenaApp);
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(string id, [FromBody] ActualizarUsuarioDto dto)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session == null) return Unauthorized();
        if (!string.Equals(session.Role, "admin", StringComparison.OrdinalIgnoreCase)) return Forbid();
        if (!Guid.TryParse(id, out _)) return BadRequest(new { error = "Usuario inválido" });
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { error = "Nombre y usuario RNDC son obligatorios" });
        if (dto.Role is not ("admin" or "operator" or "auditor"))
            return BadRequest(new { error = "Rol inválido" });
        var cambiarCorreo = !string.IsNullOrWhiteSpace(dto.CorreoEmail) || !string.IsNullOrWhiteSpace(dto.ContrasenaApp);
        if (cambiarCorreo && (string.IsNullOrWhiteSpace(dto.CorreoEmail) || string.IsNullOrWhiteSpace(dto.ContrasenaApp)))
            return BadRequest(new {error="Para cambiar correo debe ingresar correo y contraseña de aplicación"});

        if (dto.Whatsapp is not null && dto.Whatsapp.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(dto.Whatsapp,@"^\+[1-9][0-9]{7,14}$"))
            return BadRequest(new {error="Celular inválido. Incluya código de país."});
        await _db.ActualizarUsuarioAsync(id, dto.Name.Trim(), dto.Email.Trim(), dto.Password, dto.Role, dto.Active, dto.Whatsapp);
        if (cambiarCorreo)
        {
            if (string.IsNullOrWhiteSpace(dto.CorreoEmail) || string.IsNullOrWhiteSpace(dto.ContrasenaApp))
                return BadRequest(new { error = "Para cambiar correo debe ingresar correo y contraseña de aplicación" });
            await _db.GuardarCredencialEmailUsuarioAsync(id, dto.CorreoEmail, dto.ContrasenaApp);
        }
        return NoContent();
    }
}

public sealed class CredencialCorreoDto { public string CorreoEmail { get; set; } = ""; public string ContrasenaApp { get; set; } = ""; }
public sealed class ActualizarUsuarioDto
{
    public string? Whatsapp { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Password { get; set; }
    public string Role { get; set; } = "operator";
    public bool Active { get; set; } = true;
    public string? CorreoEmail { get; set; }
    public string? ContrasenaApp { get; set; }
}
