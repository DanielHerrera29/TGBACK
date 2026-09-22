using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TransportesGutierrez.Api.Services;
namespace TransportesGutierrez.Api.Controllers;
[ApiController]
[Route("api/ordenes-escolta/{orden:guid}/firma")]
public sealed class FirmaOtpController(AppSessionService sessions,FirmaOtpService otp) : ControllerBase
{
    public sealed record Solicitud(string Nombre,string Email);
    public sealed record Comprobacion(Guid Id,string Codigo);
    private IActionResult Result(JObject row) => row["error"]!=null ? Conflict(new {error=(string?)row["error"]}) : Ok(new {id=(string?)row["id"],estado=(string?)row["estado"],venceAt=(string?)row["vence_at"]});
    [HttpPost("solicitar")]
    public async Task<IActionResult> Start(Guid orden,Solicitud datos)
    {
        var session=sessions.Read(Request.Headers.Authorization);
        if(session==null) return Unauthorized();
        if(!otp.Configured) return StatusCode(503,new {error="Falta configurar la cuenta Brevo de códigos. El botón Firmar sigue bloqueado."});
        if(string.IsNullOrWhiteSpace(datos.Nombre) || datos.Nombre.Trim().Length is <3 or >150 || !FirmaOtpService.ValidEmail(datos.Email)) return BadRequest(new {error="Ingrese el nombre y correo del arquitecto."});
try {return Result(await otp.Start(session.UserId,orden.ToString(),datos.Nombre.Trim(),datos.Email!));}
        catch(HttpRequestException) {return StatusCode(503,new {error="No se pudo confirmar la solicitud. Espere diez minutos antes de iniciar otra. La orden se conserva."});}
        catch(TaskCanceledException) {return StatusCode(503,new {error="Respuesta incierta del proveedor. Espere diez minutos antes de solicitar otro código."});}
    }
    [HttpPost("estado")]
    public async Task<IActionResult> State(Guid orden) {
        var session=sessions.Read(Request.Headers.Authorization);
        if(session==null) return Unauthorized();
        var row=await otp.Operation(session.UserId,orden.ToString(),"AUTORIZAR");
        return Ok(new {autorizada=(bool?)row["autorizada"]==true});
    }
    [HttpPost("comprobar")]
    public async Task<IActionResult> Check(Guid orden,Comprobacion datos)
    {
        var session=sessions.Read(Request.Headers.Authorization);
        if(session==null) return Unauthorized();
        if(!otp.Configured) return StatusCode(503,new {error="Correo de verificación no configurado."});
        if(!Regex.IsMatch(datos.Codigo??"","^[0-9]{6}$")) return BadRequest(new {error="Ingrese los seis dígitos."});
        try {return Result(await otp.Check(session.UserId,orden.ToString(),datos.Id.ToString(),datos.Codigo!));}
        catch(HttpRequestException) {return StatusCode(503,new {error="No se pudo confirmar el resultado. La firma no está verificada; espere el vencimiento para solicitar otro código."});}
        catch(TaskCanceledException) {return StatusCode(503,new {error="Resultado incierto. No se autoriza la firma hasta completar una nueva verificación."});}
    }
}

