using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TransportesGutierrez.Api.Configurations;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/modelo-teg")]
public sealed class ModeloTegController(AppSessionService sessions, SupabaseService db,
    IHttpClientFactory factory, IOptions<SupabaseOptions> options) : ControllerBase
{
    private const int Batch = 500;
    private const int MaxExport = 100000;
    private const string Selection = "id,folio,empresa,service_type,estado_operativo,created_at,servicio_trayectos(maquina,origen,destino,placa_camabaja,peso_toneladas),ordenes_escolta_items(posicion,ordenes_escolta(id,consecutivo,codigo_orden,fecha,nombre_escolta,placa_escolta,observaciones))";

    private async Task<bool> Allowed()
    {
        var session = sessions.Read(Request.Headers.Authorization);
        if (session is null) return false;
        var role = await db.RolActivoAsync(session.UserId);
        return role == session.Role && role is "admin" or "administrativo" or "auditor";
    }

    public static string DateFilters(string? desde, string? hasta)
    {
        if (desde is null && hasta is null) return "";
        if (!DateOnly.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            || end < start || end == DateOnly.MaxValue)
            throw new ArgumentException("Seleccione fecha inicial y final válidas, en ese orden.");
        // Colombia UTC-5; incluye íntegramente el último día.
        return $"&created_at=gte.{start:yyyy-MM-dd}T05:00:00Z&created_at=lt.{end.AddDays(1):yyyy-MM-dd}T05:00:00Z";
    }

    private async Task<JsonArray> Read(string filters, string cutoff, string? cursor, int limit, CancellationToken ct)
    {
        if (cursor is not null && !Guid.TryParse(cursor, out _)) throw new ArgumentException("Cursor inválido.");
        var url = options.Value.Url.TrimEnd('/') + "/rest/v1/servicios?select=" + Uri.EscapeDataString(Selection)
            + "&order=id.asc&limit=" + limit + filters + "&created_at=lte." + Uri.EscapeDataString(cutoff)
            + (cursor is null ? "" : "&id=gt." + Uri.EscapeDataString(cursor));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("apikey", options.Value.Key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Key);
        using var response = await factory.CreateClient("Supabase").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonArray
            ?? throw new InvalidOperationException("Respuesta de servicios inválida.");
    }

    [HttpGet]
    public async Task<IActionResult> List(string? desde, string? hasta, string? cursor, string? corte, CancellationToken ct)
    {
        if (!await Allowed()) return StatusCode(403);
        try
        {
            var filters = DateFilters(desde, hasta);
            var cutoff = ParseCutoff(corte);
            var records = new JsonArray();
            var after = cursor;
            while (records.Count < 51)
            {
                var batch = await Read(filters, cutoff, after, 51-records.Count, ct);
                if (batch.Count == 0) break;
                foreach (var record in batch) records.Add(record!.DeepClone());
                var next = records.Last()!["id"]!.ToString();
                if (next == after) throw new InvalidOperationException("La consulta no avanzó. Reintente.");
                after = next;
            }
            var more = records.Count > 50;
            var rows = records.Take(50).Select(x => ModeloTegWorkbook.Project(x!)).ToArray();
            return Ok(new { rows, corte = cutoff, next = more ? rows.Last().Id : null });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (HttpRequestException) { return StatusCode(503, new { error = "No se pudieron consultar los servicios. Reintente." }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    private static string ParseCutoff(string? value)
    {
        if (value is null) return DateTimeOffset.UtcNow.ToString("o");
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("Fecha de corte inválida.");
        return date.ToUniversalTime().ToString("o");
    }

    [HttpGet("excel")]
    public async Task<IActionResult> Excel(string? desde, string? hasta, string? corte, CancellationToken ct)
    {
        if (!await Allowed()) return StatusCode(403);
        try
        {
            var filters = DateFilters(desde, hasta);
            var cutoff = ParseCutoff(corte);
            var rows = new List<TegRow>();
            string? cursor = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var batch = await Read(filters, cutoff, cursor, Batch, ct);
                if (batch.Count == 0) break;
                foreach (var entry in batch) rows.Add(ModeloTegWorkbook.Project(entry!));
                if (rows.Count > MaxExport) return BadRequest(new { error = "La exportación supera 100.000 servicios. Seleccione un rango de fechas menor; no se ha generado un archivo parcial." });
                var next = rows.Last().Id;
                if (next == cursor) throw new InvalidOperationException("La consulta no avanzó. No se generó un archivo parcial.");
                cursor = next;
                // No asumir que una página corta es la última: Supabase puede limitarla.
            }
            var bytes = ModeloTegWorkbook.Create(rows, desde, hasta, cutoff);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"MODELO_BASE_DATOS_TEG_{(desde is null ? "todos" : desde + "_" + hasta)}.xlsx");
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (HttpRequestException) { return StatusCode(503, new { error = "Falló la consulta. No se ha generado un archivo parcial." }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
