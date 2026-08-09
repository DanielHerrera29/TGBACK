using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Microsoft.Extensions.Options;
using TransportesGutierrez.Api.Configurations;
using TransportesGutierrez.Api.Dtos;
using TransportesGutierrez.Api.Models;

namespace TransportesGutierrez.Api.Services;

public class SupabaseService
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        },
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly HttpClient _http;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseService> _logger;

    public SupabaseService(
        IHttpClientFactory httpFactory,
        IOptions<SupabaseOptions> options,
        ILogger<SupabaseService> logger)
    {
        _http = httpFactory.CreateClient("Supabase");
        _options = options.Value;
        _logger = logger;
    }

    private string SupabaseUrl => _options.Url.TrimEnd('/');
    private string SupabaseKey => _options.Key;

    private void SetHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("apikey", SupabaseKey);
        request.Headers.Add("Authorization", $"Bearer {SupabaseKey}");
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/rest/v1/settings?select=id&limit=1");
        SetHeaders(request);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> RpcCallAsync(string functionName, object? parameters = null)
    {
        var url = $"{SupabaseUrl}/rest/v1/rpc/{functionName}";
        var json = parameters != null ? JsonConvert.SerializeObject(parameters, _jsonSettings) : "{}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        SetHeaders(request);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<T?> GetAsync<T>(string table, string? query = null) where T : class
    {
        var url = $"{SupabaseUrl}/rest/v1/{table}";
        if (!string.IsNullOrEmpty(query)) url += $"?{query}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var list = JsonConvert.DeserializeObject<List<T>>(json, _jsonSettings);
        return list?.FirstOrDefault() ?? Activator.CreateInstance<T>();
    }

    private async Task<string> InsertAsync<T>(string table, T data)
    {
        var url = $"{SupabaseUrl}/rest/v1/{table}";
        var json = JsonConvert.SerializeObject(data, _jsonSettings);

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        SetHeaders(request);
        request.Headers.Add("Prefer", "return=representation");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Supabase error {Status} en {Table}: {Error}",
                (int)response.StatusCode, table, errorBody);
            return string.Empty;
        }

        var resultJson = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(resultJson);
        return result?.FirstOrDefault()?.GetValueOrDefault("id")?.ToString() ?? "";
    }

    private async Task UpdateAsync<T>(string table, string id, T data)
    {
        var url = $"{SupabaseUrl}/rest/v1/{table}?id=eq.{id}";
        var json = JsonConvert.SerializeObject(data, _jsonSettings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        SetHeaders(request);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        var url = $"{SupabaseUrl}/rest/v1/settings?limit=1";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Error obteniendo settings: {Status} {Body}",
                (int)response.StatusCode, body);
            return new AppSettings();
        }

        var list = JsonConvert.DeserializeObject<List<AppSettings>>(body, _jsonSettings);
        return list?.FirstOrDefault() ?? new AppSettings();
    }

    public async Task<string> GenerarConsecutivoRemesaAsync()
    {
        var result = await RpcCallAsync("generar_consecutivo_remesa");
        return result.Trim('"');
    }

    public async Task<string> GenerarConsecutivoManifiestoAsync()
    {
        var result = await RpcCallAsync("generar_consecutivo_manifiesto");
        return result.Trim('"');
    }

    public async Task<string> CrearRemesaDraftAsync(GenerarRemesaDto dto, string consecutivo, string xml)
    {
        var remesa = new Remesa
        {
            Id = Guid.NewGuid().ToString(),
            Consecutivo = consecutivo,
            GeneradorNit = dto.GeneradorNit ?? "",
            GeneradorTipoId = dto.GeneradorTipoId ?? "N",
            GeneradorSede = dto.GeneradorSede ?? "00",
            RemitenteNit = dto.RemitenteNit ?? dto.GeneradorNit ?? "",
            RemitenteTipoId = dto.RemitenteTipoId ?? dto.GeneradorTipoId ?? "N",
            RemitenteSede = dto.RemitenteSede ?? dto.GeneradorSede ?? "00",
            RemitenteMunicipioDane = dto.RemitenteMunicipioDane ?? "11001000",
            DestinatarioNit = dto.DestinatarioNit ?? "",
            DestinatarioTipoId = dto.DestinatarioTipoId ?? "N",
            DestinatarioSede = dto.DestinatarioSede ?? "00",
            DestinatarioMunicipioDane = dto.DestinatarioMunicipioDane ?? "11001000",
            NaturalezaCarga = dto.NaturalezaCarga ?? "1",
            Cantidad = dto.Cantidad,
            UnidadMedida = dto.UnidadMedida ?? "1",
            TipoEmpaque = dto.TipoEmpaque,
            CodigoProducto = dto.CodigoProducto,
            DescripcionProducto = dto.DescripcionProducto,
            PesoKg = dto.PesoKg,
            TipoOperacion = dto.TipoOperacion ?? "G",
            PolizaNumero = dto.PolizaNumero,
            PolizaVencimiento = dto.PolizaVencimiento,
            PolizaAseguradora = dto.PolizaAseguradora,
            PolizaAseguradoraNit = dto.PolizaAseguradoraNit,
            RawMessage = dto.RawMessage,
            ClienteNombre = dto.ClienteNombre,
            Obra = dto.Obra,
            Observaciones = dto.Observaciones,
            XmlEnviado = xml,
            Estado = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return await InsertAsync("remesas", remesa);
    }

    public async Task ActualizarRemesaRndcAsync(string id, string? radicado, string xml, string xmlResp, string estado, string? error = null)
    {
        var update = new Dictionary<string, object?>
        {
            ["radicado_rndc"] = radicado,
            ["xml_enviado"] = xml,
            ["xml_respuesta"] = xmlResp,
            ["estado"] = estado,
            ["error_detalle"] = error,
            ["updated_at"] = DateTime.UtcNow.ToString("o")
        };

        await UpdateAsync("remesas", id, update);
    }

    public async Task<string> CrearManifiestoDraftAsync(GenerarManifiestoDto dto, string remesaId, string consecutivo, string xml)
    {
        var manifiestoId = Guid.NewGuid().ToString();
        // Usar objeto anónimo para excluir valor_saldo (columna GENERATED en Postgres)
        var insertData = new
        {
            id = manifiestoId,
            consecutivo,
            remesa_id = remesaId,
            placa_vehiculo = dto.PlacaVehiculo ?? "",
            placa_remolque = dto.PlacaRemolque,
            conductor_tipo_id = dto.ConductorTipoId ?? "C",
            conductor_cedula = dto.ConductorCedula,
            conductor2_cedula = dto.Conductor2Cedula,
            propietario_tipo_id = dto.PropietarioTipoId,
            propietario_cedula = dto.PropietarioCedula,
            fecha_despacho = dto.FechaDespacho,
            fecha_limite_entrega = dto.FechaLimiteEntrega,
            tipo_valor_pactado = dto.TipoValorPactado ?? "B",
            valor_viaje = dto.ValorViaje,
            valor_anticipo = dto.ValorAnticipo,
            municipio_pago_dane = dto.MunicipioPagoDane,
            fecha_limite_pago = dto.FechaLimitePago,
            resp_cargue = dto.RespCargue ?? "D",
            resp_descargue = dto.RespDescargue ?? "D",
            horas_espera_cargue = dto.HorasEsperaCargue,
            horas_espera_descargue = dto.HorasEsperaDescargue,
            observaciones = dto.Observaciones,
            xml_enviado = xml,
            estado = "draft",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };

        return await InsertAsync("manifiestos", insertData);
    }

    public async Task ActualizarManifiestoRndcAsync(string id, string? radicado, string xml, string xmlResp, string estado, string? error = null)
    {
        var update = new Dictionary<string, object?>
        {
            ["radicado_rndc"] = radicado,
            ["numero_autorizacion"] = radicado,
            ["xml_enviado"] = xml,
            ["xml_respuesta"] = xmlResp,
            ["estado"] = estado,
            ["error_detalle"] = error,
            ["updated_at"] = DateTime.UtcNow.ToString("o")
        };

        await UpdateAsync("manifiestos", id, update);
    }

    public async Task<Remesa?> GetRemesaAsync(string remesaId)
    {
        var url = $"{SupabaseUrl}/rest/v1/remesas?id=eq.{remesaId}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var list = JsonConvert.DeserializeObject<List<Remesa>>(json, _jsonSettings);
        return list?.FirstOrDefault();
    }

    public async Task<List<Remesa>> GetRemesasPendientesAsync()
    {
        var url = $"{SupabaseUrl}/rest/v1/remesas?estado=eq.generated&select=*";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var remesas = JsonConvert.DeserializeObject<List<Remesa>>(body, _jsonSettings) ?? new();

        var manifUrl = $"{SupabaseUrl}/rest/v1/manifiestos?select=remesa_id";
        var manifRequest = new HttpRequestMessage(HttpMethod.Get, manifUrl);
        SetHeaders(manifRequest);
        var manifResponse = await _http.SendAsync(manifRequest);
        manifResponse.EnsureSuccessStatusCode();
        var manifBody = await manifResponse.Content.ReadAsStringAsync();
        var manifList = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(manifBody, _jsonSettings) ?? new();
        var usedIds = manifList.Select(m => m.GetValueOrDefault("remesa_id")?.ToString() ?? "").ToHashSet();

        return remesas.Where(r => !usedIds.Contains(r.Id)).ToList();
    }

    public async Task<(string Id, string Role)?> ValidarUsuarioAppAsync(string email, string password)
    {
        var safeEmail = Uri.EscapeDataString(email);
        var safePassword = Uri.EscapeDataString(password);
        var url = $"{SupabaseUrl}/rest/v1/users?select=id,role&email=eq.{safeEmail}&password=eq.{safePassword}&active=eq.true&limit=1";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync();
        var users = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(body, _jsonSettings);
        var user = users?.FirstOrDefault();
        return user == null
            ? null
            : (user.GetValueOrDefault("id")?.ToString() ?? "", user.GetValueOrDefault("role")?.ToString() ?? "operator");
    }

    public async Task GuardarCredencialEmailUsuarioAsync(string userId, string correoEmail, string contrasenaApp)
    {
        var url = $"{SupabaseUrl}/rest/v1/rpc/guardar_credencial_email_usuario";
        var body = new { p_usuario_id = userId, p_correo_email = correoEmail, p_contrasena_app = contrasenaApp };
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
        };
        SetHeaders(request);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task ActualizarUsuarioAsync(string userId, string name, string email, string? password, string role, bool active)
    {
        var url = $"{SupabaseUrl}/rest/v1/users?id=eq.{Uri.EscapeDataString(userId)}";
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email,
            ["role"] = role,
            ["active"] = active
        };
        if (!string.IsNullOrWhiteSpace(password)) body["password"] = password;
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
        };
        SetHeaders(request);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<OrdenEscoltaCreada> CrearOrdenEscoltaAsync(string userId, CrearOrdenEscoltaDto dto)
    {
        var ordenData = new
        {
            fecha = dto.Fecha.ToString("yyyy-MM-dd"),
            empresa = dto.Empresa.Trim(),
            placa_camabaja = dto.PlacaCamabaja.Trim().ToUpperInvariant(),
            placa_escolta = string.IsNullOrWhiteSpace(dto.PlacaEscolta) ? null : dto.PlacaEscolta.Trim().ToUpperInvariant(),
            nombre_escolta = string.IsNullOrWhiteSpace(dto.NombreEscolta) ? null : dto.NombreEscolta.Trim(),
            observaciones = string.IsNullOrWhiteSpace(dto.Observaciones) ? null : dto.Observaciones.Trim(),
            created_by = userId
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/ordenes_escolta")
        {
            Content = new StringContent(JsonConvert.SerializeObject(ordenData), Encoding.UTF8, "application/json")
        };
        SetHeaders(request);
        request.Headers.Add("Prefer", "return=representation");
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(await response.Content.ReadAsStringAsync()) ?? new();
        var row = rows.FirstOrDefault() ?? throw new InvalidOperationException("Supabase no devolvio la orden creada.");
        var id = row.GetValueOrDefault("id")?.ToString() ?? throw new InvalidOperationException("La orden creada no tiene identificador.");
        if (!long.TryParse(row.GetValueOrDefault("consecutivo")?.ToString(), out var consecutivo))
            throw new InvalidOperationException("La orden creada no tiene consecutivo.");

        var viajes = dto.Viajes.Select((viaje, index) => new
        {
            orden_id = id,
            posicion = index + 1,
            maquina = viaje.Maquina.Trim(),
            origen = viaje.Origen.Trim(),
            destino = viaje.Destino.Trim()
        }).ToList();
        var itemsRequest = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/ordenes_escolta_items")
        {
            Content = new StringContent(JsonConvert.SerializeObject(viajes), Encoding.UTF8, "application/json")
        };
        SetHeaders(itemsRequest);
        using var itemsResponse = await _http.SendAsync(itemsRequest);
        itemsResponse.EnsureSuccessStatusCode();
        return new OrdenEscoltaCreada(id, consecutivo);
    }

    public async Task<OrdenEscoltaRegistrada?> ObtenerOrdenEscoltaAsync(string id)
    {
        var url = $"{SupabaseUrl}/rest/v1/ordenes_escolta?select=id,consecutivo,created_by,pdf_path&id=eq.{Uri.EscapeDataString(id)}&limit=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(await response.Content.ReadAsStringAsync()) ?? new();
        var row = rows.FirstOrDefault();
        if (row is null || !long.TryParse(row.GetValueOrDefault("consecutivo")?.ToString(), out var consecutivo)) return null;
        return new OrdenEscoltaRegistrada(
            row.GetValueOrDefault("id")?.ToString() ?? "",
            consecutivo,
            row.GetValueOrDefault("created_by")?.ToString() ?? "",
            row.GetValueOrDefault("pdf_path")?.ToString());
    }

    public async Task<CredencialEmail?> ObtenerCredencialEmailUsuarioAsync(string userId)
    {
        var json = await RpcCallAsync("obtener_credencial_email_usuario", new { p_usuario_id = userId });
        var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json, _jsonSettings) ?? new();
        var row = rows.FirstOrDefault();
        var correo = row?.GetValueOrDefault("correo_email")?.ToString();
        var clave = row?.GetValueOrDefault("contrasena_app")?.ToString();
        return string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(clave)
            ? null
            : new CredencialEmail(correo, clave);
    }

    public Task MarcarOrdenEscoltaEnviadaAsync(string id) =>
        ActualizarOrdenEscoltaAsync(id, new { email_enviado_at = DateTime.UtcNow, email_error = (string?)null });

    public Task RegistrarErrorEmailOrdenEscoltaAsync(string id, string error) =>
        ActualizarOrdenEscoltaAsync(id, new { email_error = error });

    public async Task SubirPdfOrdenEscoltaAsync(OrdenEscoltaRegistrada orden, byte[] pdf)
    {
        var path = $"ordenes/{orden.Id}/orden_{orden.Consecutivo:D5}.pdf";
        var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SupabaseUrl}/storage/v1/object/ordenes-escolta/{encodedPath}")
        {
            Content = new ByteArrayContent(pdf)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        request.Headers.Add("x-upsert", "true");
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await ActualizarOrdenEscoltaAsync(orden.Id, new
        {
            pdf_path = path,
            pdf_tamano_bytes = pdf.LongLength,
            pdf_generado_at = DateTime.UtcNow,
            pdf_eliminado_at = (DateTime?)null
        });
    }

    public async Task<List<Dictionary<string, object>>> GetOrdenesEscoltaAsync(string userId, bool esAdmin)
    {
        var query = "select=id,consecutivo,fecha,empresa,placa_camabaja,placa_escolta,nombre_escolta,created_at,email_enviado_at,email_error,pdf_path,pdf_generado_at&order=created_at.desc";
        if (!esAdmin) query += $"&created_by=eq.{Uri.EscapeDataString(userId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/rest/v1/ordenes_escolta?{query}");
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(await response.Content.ReadAsStringAsync(), _jsonSettings) ?? new();
    }

    public async Task<string?> CrearUrlFirmadaPdfOrdenAsync(string path)
    {
        var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{SupabaseUrl}/storage/v1/object/sign/ordenes-escolta/{encodedPath}")
        {
            Content = new StringContent("{\"expiresIn\":600}", Encoding.UTF8, "application/json")
        };
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        var row = JsonConvert.DeserializeObject<Dictionary<string, object>>(await response.Content.ReadAsStringAsync(), _jsonSettings);
        var signedUrl = row?.GetValueOrDefault("signedURL")?.ToString();
        if (string.IsNullOrWhiteSpace(signedUrl)) return null;
        return signedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? signedUrl
            : $"{SupabaseUrl}/storage/v1{signedUrl}";
    }

    public async Task<int> EliminarPdfsOrdenesExpiradosAsync(CancellationToken cancellationToken = default)
    {
        var limite = DateTime.UtcNow.AddDays(-31).ToString("o");
        var url = $"{SupabaseUrl}/rest/v1/ordenes_escolta?select=id,pdf_path&pdf_path=not.is.null&pdf_generado_at=lt.{Uri.EscapeDataString(limite)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var orders = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(await response.Content.ReadAsStringAsync(cancellationToken), _jsonSettings) ?? new();
        var deleted = 0;
        foreach (var order in orders)
        {
            var id = order.GetValueOrDefault("id")?.ToString();
            var path = order.GetValueOrDefault("pdf_path")?.ToString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path)) continue;
            var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{SupabaseUrl}/storage/v1/object/ordenes-escolta/{encodedPath}");
            SetHeaders(deleteRequest);
            using var deleteResponse = await _http.SendAsync(deleteRequest, cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode) continue;
            await ActualizarOrdenEscoltaAsync(id, new { pdf_path = (string?)null, pdf_eliminado_at = DateTime.UtcNow });
            deleted++;
        }
        return deleted;
    }

    private async Task ActualizarOrdenEscoltaAsync(string id, object data)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"{SupabaseUrl}/rest/v1/ordenes_escolta?id=eq.{Uri.EscapeDataString(id)}")
        {
            Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json")
        };
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<Remesa>> GetRemesasAsync()
    {
        var url = $"{SupabaseUrl}/rest/v1/remesas?select=*&order=created_at.desc";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<List<Remesa>>(json, _jsonSettings) ?? new();
    }

    public async Task<List<Manifiesto>> GetManifiestosAsync()
    {
        var url = $"{SupabaseUrl}/rest/v1/manifiestos?select=*&order=created_at.desc";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<List<Manifiesto>>(json, _jsonSettings) ?? new();
    }

    public async Task<Manifiesto?> GetManifiestoByRemesaAsync(string remesaId)
    {
        var encodedId = Uri.EscapeDataString(remesaId);
        var url = $"{SupabaseUrl}/rest/v1/manifiestos?remesa_id=eq.{encodedId}&order=updated_at.desc&limit=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var list = JsonConvert.DeserializeObject<List<Manifiesto>>(json, _jsonSettings);
        return list?.FirstOrDefault();
    }

    public async Task<Manifiesto?> GetManifiestoAsync(string manifiestoId)
    {
        var url = $"{SupabaseUrl}/rest/v1/manifiestos?id=eq.{manifiestoId}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetHeaders(request);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var list = JsonConvert.DeserializeObject<List<Manifiesto>>(json, _jsonSettings);
        return list?.FirstOrDefault();
    }
}

public sealed record OrdenEscoltaCreada(string Id, long Consecutivo);
public sealed record OrdenEscoltaRegistrada(string Id, long Consecutivo, string CreatedBy, string? PdfPath);
public sealed record CredencialEmail(string CorreoEmail, string ContrasenaApp);
