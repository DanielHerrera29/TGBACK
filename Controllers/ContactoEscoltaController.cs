using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TransportesGutierrez.Api.Configurations;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/ordenes-escolta/contacto")]
public sealed class ContactoEscoltaController(AppSessionService sessions, IHttpClientFactory factory, IOptions<SupabaseOptions> options) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Obtener([FromQuery] Guid? usuario, CancellationToken ct)
    {
        var session = sessions.Read(Request.Headers.Authorization);
        if (session is null) return Task.FromResult<IActionResult>(Unauthorized());
        if (usuario.HasValue && usuario.Value.ToString()!=session.UserId && session.Role!="admin")
            return Task.FromResult<IActionResult>(StatusCode(403));
        return Send(HttpMethod.Post,"rpc/perfil_usuario_escolta",new {p_usuario=session.UserId,p_consulta=usuario},ct);
    }

    [HttpPut("vehiculos/{usuario:guid}")]
    public Task<IActionResult> Vehiculos(Guid usuario, [FromBody] VehiculosEscoltaDto dto, CancellationToken ct)
    {
        var session = sessions.Read(Request.Headers.Authorization);
        if (session is null) return Task.FromResult<IActionResult>(Unauthorized());
        if (session.Role!="admin") return Task.FromResult<IActionResult>(StatusCode(403));
        if (dto.Placas is null || dto.Placas.Length>30 || dto.Placas.Any(p=>p is null || !Regex.IsMatch(p,"^[A-Z]{3}[0-9]{3}$")))
            return Task.FromResult<IActionResult>(BadRequest(new {error="Revise las placas: tres letras y tres números."}));
        return Send(HttpMethod.Post,"rpc/editar_vehiculos_usuario",new {p_admin=session.UserId,p_usuario=usuario,p_placas=dto.Placas},ct);
    }

    [HttpPut("destino")]
    public Task<IActionResult> Destino([FromBody] DestinoWhatsappDto dto, CancellationToken ct)
    {
        var session = sessions.Read(Request.Headers.Authorization);
        if (session is null) return Task.FromResult<IActionResult>(Unauthorized());
        if (session.Role!="admin") return Task.FromResult<IActionResult>(StatusCode(403));
        if (dto.Destino is null || !Regex.IsMatch(dto.Destino,@"^\+[1-9][0-9]{7,14}$"))
            return Task.FromResult<IActionResult>(BadRequest(new {error="Ingrese el destino con código de país."}));
        return Send(HttpMethod.Patch,"orden_whatsapp_config?id=eq.true",new {destino=dto.Destino,updated_at=DateTime.UtcNow},ct);
    }

    private async Task<IActionResult> Send(HttpMethod method,string path,object data,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,$"{options.Value.Url.TrimEnd('/')}/rest/v1/{path}");
        request.Headers.Add("apikey",options.Value.Key);
        request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {options.Value.Key}");
        request.Content=JsonContent.Create(data);
        try {
            using var response=await factory.CreateClient("Supabase").SendAsync(request,ct);
            var body=await response.Content.ReadAsStringAsync(ct);
            if(response.IsSuccessStatusCode) return Content(string.IsNullOrWhiteSpace(body)?"null":body,"application/json");
            using var error=JsonDocument.Parse(body);
            var code=error.RootElement.TryGetProperty("code",out var c)?c.GetString():null;
            return StatusCode(code=="42501"?403:503,new {error="No se pudo consultar o guardar el contacto. Verifique la migración de contactos y vuelva a intentar."});
        } catch(HttpRequestException) { return StatusCode(503,new {error="No se pudo conectar con el catálogo de contactos."}); }
        catch(JsonException) { return StatusCode(503,new {error="Respuesta de contactos no válida."}); }
    }
}
public sealed class VehiculosEscoltaDto { public string[]? Placas {get;set;} }
public sealed class DestinoWhatsappDto { public string? Destino {get;set;} }
