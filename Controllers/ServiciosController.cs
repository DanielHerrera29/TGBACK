using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TransportesGutierrez.Api.Configurations;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/servicios")]
public sealed class ServiciosController : ControllerBase
{
    private readonly AppSessionService _sessions;
    private readonly HttpClient _http;
    private readonly SupabaseOptions _options;
    public ServiciosController(AppSessionService sessions, IHttpClientFactory factory, IOptions<SupabaseOptions> options)
    { _sessions = sessions; _http = factory.CreateClient("Supabase"); _options = options.Value; }

    [HttpPost("orden-borrador")]
    public Task<IActionResult> Guardar([FromBody] GuardarBorradorRequest request, CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Task.FromResult<IActionResult>(Unauthorized());
        if (request.Clave == Guid.Empty || request.Datos.ValueKind != JsonValueKind.Object)
            return Task.FromResult<IActionResult>(BadRequest(new { error = "Solicitud inválida" }));
        // Usuario siempre del token verificado; la función consulta rol/activo actuales.
        return Rpc("guardar_orden_servicios", new { p_usuario = session.UserId, p_clave = request.Clave, p_datos = request.Datos }, ct);
    }

    [HttpGet]
    public Task<IActionResult> Listar(CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        return session is null ? Task.FromResult<IActionResult>(Unauthorized())
            : Rpc("listar_servicios_operativos", new { p_usuario = session.UserId }, ct);
    }

    [HttpGet("orden-borrador/{clientOrderId:guid}")]
    public Task<IActionResult> Recuperar(Guid clientOrderId, CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        return session is null ? Task.FromResult<IActionResult>(Unauthorized())
            : Rpc("recuperar_orden_servicios", new { p_usuario = session.UserId, p_client_order = clientOrderId }, ct);
    }

    [HttpGet("clientes")]
    public Task<IActionResult> Clientes(CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        return session is null ? Task.FromResult<IActionResult>(Unauthorized())
            : Rpc("clientes_para_orden", new { p_usuario = session.UserId }, ct);
    }

    [HttpGet("clientes/vehiculos-disponibles")]
    public Task<IActionResult> VehiculosDisponibles(CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        return session is null ? Task.FromResult<IActionResult>(Unauthorized())
            : Rpc("vehiculos_para_vincular_cliente", new { p_usuario = session.UserId }, ct);
    }

    [HttpPut("clientes/{cliente:guid}/vehiculos/{vehiculo:guid}")]
    public Task<IActionResult> VincularVehiculo(Guid cliente, Guid vehiculo, CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        return session is null ? Task.FromResult<IActionResult>(Unauthorized())
            : Rpc("vincular_vehiculo_cliente", new { p_usuario = session.UserId, p_cliente = cliente, p_vehiculo = vehiculo }, ct);
    }

    [HttpPost("clientes")]
    public async Task<IActionResult> CrearCliente([FromBody] CrearClienteRequest data, CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        if (session is null) return Unauthorized();
        if (data.Placas is not null)
            return await Rpc("registrar_cliente_placas", new {
                p_usuario = session.UserId, p_id = data.Id, p_tipo = data.Tipo,
                p_nombre = data.Nombre, p_documento = data.Documento,
                p_placas = data.Placas.Select(p => p?.Trim().ToUpperInvariant()).ToArray()
            }, ct);
        data.Placa = string.IsNullOrWhiteSpace(data.Placa) ? null : data.Placa.Trim().ToUpperInvariant();
        if (data.Placa is not null && !System.Text.RegularExpressions.Regex.IsMatch(data.Placa, @"^[A-Z]{3}[0-9]{3}$"))
            return BadRequest(new { error = "Placa inválida: use tres letras y tres números." });
        if (data.Id == Guid.Empty || data.Tipo is not ("empresa" or "persona") || string.IsNullOrWhiteSpace(data.Nombre)
            || data.Nombre.Length > 150 || !System.Text.RegularExpressions.Regex.IsMatch(data.Documento ?? "", @"^\d{5,20}$"))
            return BadRequest(new { error = "Revise nombre, tipo y documento." });
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, $"{_options.Url.TrimEnd('/')}/rest/v1/{path}");
            request.Headers.Add("apikey", _options.Key);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.Key}");
            if (body is not null) {
                request.Headers.Add("Prefer", "return=representation");
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
            return await _http.SendAsync(request, ct);
        }
        using var userResponse = await Send(HttpMethod.Get, $"users?select=role&id=eq.{Uri.EscapeDataString(session.UserId)}&active=eq.true");
        if (!userResponse.IsSuccessStatusCode) return StatusCode(503);
        using var users = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(ct));
        if (users.RootElement.GetArrayLength() != 1 || users.RootElement[0].GetProperty("role").GetString() is not ("admin" or "administrativo"))
            return StatusCode(403);
        // Stable client id makes retries return the original result without another insert.
        using var existingResponse = await Send(HttpMethod.Get, $"clientes?select=*&id=eq.{data.Id}");
        if (!existingResponse.IsSuccessStatusCode) return StatusCode(503);
        using var existing = JsonDocument.Parse(await existingResponse.Content.ReadAsStringAsync(ct));
        if (existing.RootElement.GetArrayLength() > 0) {
            var row = existing.RootElement[0];
            if (row.GetProperty("nit_o_documento").GetString() != data.Documento || row.GetProperty("nombre").GetString() != data.Nombre.Trim()
                || row.GetProperty("tipo_cliente").GetString() != data.Tipo
                || (row.TryGetProperty("placa_carga", out var placaAnterior) ? placaAnterior.GetString() : null) != data.Placa) return Conflict();
            return Content(row.GetRawText(), "application/json");
        }
        var campos = new Dictionary<string,object?> {
            ["id"] = data.Id, ["tipo_cliente"] = data.Tipo, ["nombre"] = data.Nombre.Trim(),
            ["razon_social"] = data.Tipo == "empresa" ? data.Nombre.Trim() : null,
            ["nit_o_documento"] = data.Documento, ["activo"] = true
        };
        if (data.Placa is not null) campos["placa_carga"] = data.Placa;
        using var created = await Send(HttpMethod.Post, "clientes", campos);
        if (created.StatusCode == System.Net.HttpStatusCode.Conflict) return Conflict();
        if (!created.IsSuccessStatusCode) return StatusCode(503);
        using var result = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
        return Content(result.RootElement[0].GetRawText(), "application/json");
    }
    [HttpGet("usuarios-gestion")]
    public Task<IActionResult> UsuariosGestion(CancellationToken ct)
    {
        var session = _sessions.Read(Request.Headers.Authorization);
        return session is null ? Task.FromResult<IActionResult>(Unauthorized())
            : Rpc("usuarios_para_gestion", new { p_usuario = session.UserId }, ct);
    }

    private async Task<IActionResult> Rpc(string function, object parameters, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_options.Url.TrimEnd('/')}/rest/v1/rpc/{function}");
        req.Headers.Add("apikey", _options.Key);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.Key}");
        req.Content = new StringContent(JsonSerializer.Serialize(parameters), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await _http.SendAsync(req, ct); }
        catch (HttpRequestException) { return StatusCode(503, new { error = "Conexión interrumpida. Conserve la solicitud pendiente." }); }
        using var responseScope = response;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode) return Content(body, "application/json");
        JsonDocument error;
        try { error = JsonDocument.Parse(body); }
        catch (JsonException) { return StatusCode(503, new { error = "Respuesta no válida de la base. Conserve la solicitud pendiente." }); }
        using var errorScope = error;
        var code = error.RootElement.TryGetProperty("code", out var c) ? c.GetString() : "";
        var status = code switch { "42501" => 403, "P0001" => 409, "22023" or "22P02" or "23505" => 400, _ => 503 };
        return StatusCode(status, new { error = status switch {
            403 => "Sin permiso para esta operación.",
            409 => "El borrador cambió o la clave ya fue utilizada. Recupere la operación pendiente.",
            400 => function == "registrar_cliente_placas" ? "Revise nombre, documento y placas. El documento puede estar registrado en otro cliente." : "Revise los datos y los identificadores de los viajes.",
            _ => "No se pudo guardar. Conserve el borrador y reintente; verifique la migración del servidor."
        }});
    }
}

public sealed class GuardarBorradorRequest
{
    public Guid Clave { get; set; }
    public JsonElement Datos { get; set; }
}

public sealed class CrearClienteRequest
{
    public string[]? Placas { get; set; }
    public string? Placa { get; set; }
    public Guid Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Documento { get; set; } = "";
}
