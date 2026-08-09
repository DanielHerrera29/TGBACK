using Microsoft.AspNetCore.Mvc;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/sesion")]
public class SesionController : ControllerBase
{
    private readonly SupabaseService _db;
    private readonly AppSessionService _sessions;
    public SesionController(SupabaseService db, AppSessionService sessions) => (_db, _sessions) = (db, sessions);

    [HttpPost("iniciar")]
    public async Task<IActionResult> Iniciar([FromBody] IniciarSesionDto dto)
    {
        var user = await _db.ValidarUsuarioAppAsync(dto.Email, dto.Password);
        if (user == null) return Unauthorized(new { error = "Credenciales inválidas" });
        return Ok(new { token = _sessions.Create(user.Value.Id, user.Value.Role) });
    }
}

public sealed class IniciarSesionDto { public string Email { get; set; } = ""; public string Password { get; set; } = ""; }
