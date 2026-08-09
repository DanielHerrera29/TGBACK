using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using TransportesGutierrez.Api.Dtos;
using TransportesGutierrez.Api.Models;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/remesa")]
public class RemesaController : ControllerBase
{
    private readonly XmlGeneratorService _xmlGen;
    private readonly RndcClient _rndc;
    private readonly SupabaseService _db;

    public RemesaController(XmlGeneratorService xmlGen, RndcClient rndc, SupabaseService db)
    {
        _xmlGen = xmlGen;
        _rndc = rndc;
        _db = db;
    }

    private static string Or(string? val, string fallback)
        => string.IsNullOrWhiteSpace(val) ? fallback : val;

    private static string NormalizeTipoOperacion(string? value)
        => Or(value, "G").ToUpperInvariant() switch
        {
            "GENERAL" => "G",
            "GENERADOR" => "G",
            "N" => "G",
            _ => Or(value, "G").ToUpperInvariant()
        };

    private static string NormalizeNaturalezaCarga(string? value)
        => Or(value, "1").ToUpperInvariant() switch
        {
            "NORMAL" => "1",
            "GENERAL" => "1",
            "N" => "1",
            _ => Or(value, "1")
        };

    private static string NormalizeUnidadMedida(string? value)
        => Or(value, "1").ToUpperInvariant() switch
        {
            "KG" => "1",
            "KILO" => "1",
            "KILOS" => "1",
            "KILOGRAMO" => "1",
            "KILOGRAMOS" => "1",
            _ => Or(value, "1")
        };

    private static string NormalizeTipoEmpaque(string? value)
    {
        var raw = Or(value, "4").Trim().ToUpperInvariant();
        return raw switch
        {
            "04" => "4",
            "BD" => "4",
            "CA" => "4",
            _ => raw.TrimStart('0') is { Length: > 0 } normalized ? normalized : "4"
        };
    }

    private static string NormalizeCodigoProducto(string? value)
    {
        var raw = Or(value, "001806").Trim();
        return string.IsNullOrWhiteSpace(raw) ? "001806" : raw;
    }

    private static string FechaDescarguePorDefecto(string? fechaCargue)
    {
        if (DateTime.TryParseExact(
                fechaCargue,
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var cargue))
        {
            return cargue.AddDays(1).ToString("dd/MM/yyyy");
        }

        return DateTime.Today.AddDays(1).ToString("dd/MM/yyyy");
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var remesas = await _db.GetRemesasAsync();
        var manifiestos = await _db.GetManifiestosAsync();
        var porRemesa = manifiestos
            .GroupBy(m => m.RemesaId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UpdatedAt).First());

        return Ok(remesas.Select(r =>
        {
            porRemesa.TryGetValue(r.Id, out var manifiesto);
            return new
            {
                remesa = ToResponse(r),
                estado_manifiesto = manifiesto?.Estado,
                error_manifiesto = manifiesto?.ErrorDetalle
            };
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Obtener(string id)
    {
        var remesa = await _db.GetRemesaAsync(id);
        if (remesa == null) return NotFound(new { error = "Remesa no encontrada" });

        var manifiesto = await _db.GetManifiestoByRemesaAsync(id);
        return Ok(new
        {
            remesa = ToResponse(remesa),
            manifiesto = manifiesto == null ? null : ToManifiestoResponse(manifiesto)
        });
    }

    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes()
    {
        var remesas = await _db.GetRemesasPendientesAsync();
        return Ok(remesas.Select(r => new {
            id              = r.Id,
            consecutivo     = r.Consecutivo,
            cliente_nombre  = r.ClienteNombre,
            estado          = r.Estado,
            peso_kg         = r.PesoKg,
            radicado_rndc   = r.RadicadoRndc,
            created_at      = r.CreatedAt
        }));
    }

    [HttpPost("generar")]
    public async Task<IActionResult> Generar([FromBody] GenerarRemesaDto dto)
    {
        var settings = await _db.GetSettingsAsync();
        var consecutivo = await _db.GenerarConsecutivoRemesaAsync();

        // Fallbacks: trata "" igual que null
        var generadorNit = Or(dto.GeneradorNit, Or(settings.GeneradorNit, ""));
        var generadorTipoId = Or(dto.GeneradorTipoId, Or(settings.GeneradorTipoId, "N"));
        var generadorSede = Or(dto.GeneradorSede, Or(settings.GeneradorSede, "00"));
        var remitenteNit = Or(dto.RemitenteNit, generadorNit);
        var remitenteSede = Or(dto.RemitenteSede, generadorSede);
        var propietarioNit = Or(dto.PropietarioNit, generadorNit);
        var propietarioTipoId = Or(dto.PropietarioTipoId, generadorTipoId);
        var propietarioSede = Or(dto.PropietarioSede, generadorSede);
        var remitenteMunicipioDane = Or(dto.RemitenteMunicipioDane, settings.EmpresaMunicipioDane);
        var destinatarioMunicipioDane = Or(dto.DestinatarioMunicipioDane, settings.EmpresaMunicipioDane);
        var cantidad = dto.Cantidad ?? dto.PesoKg;
        var tipoOperacion = NormalizeTipoOperacion(dto.TipoOperacion);
        var naturalezaCarga = NormalizeNaturalezaCarga(dto.NaturalezaCarga);
        var unidadMedida = NormalizeUnidadMedida(dto.UnidadMedida);
        var tipoEmpaque = NormalizeTipoEmpaque(dto.TipoEmpaque);
        var codigoProducto = NormalizeCodigoProducto(dto.CodigoProducto);
        var descripcionProducto = Or(dto.DescripcionProducto, "MAQUINARIA").ToUpperInvariant();
        var fechaCitaCargue = Or(dto.FechaCitaCargue, DateTime.Today.ToString("dd/MM/yyyy"));
        var fechaCitaDescargue = Or(
            dto.FechaCitaDescargue,
            FechaDescarguePorDefecto(fechaCitaCargue));
        var destinatarioNit = Or(dto.DestinatarioNit, "");
        if (destinatarioNit.Length < 6)
        {
            return BadRequest(new
            {
                exito = false,
                error = "Ingrese un NIT de destinatario completo antes de enviar al RNDC. Para pruebas use 9001112221."
            });
        }

        var req = new RndcRequest
        {
            Username = dto.RndcUsername,
            Password = dto.RndcPassword,
            Simulacion = settings.Simulacion,
            ProcesoId = 3,
            Variables = new Dictionary<string, string>
            {
                ["nit_empresa"]          = settings.EmpresaNit,
                ["consecutivo"]          = consecutivo,
                ["tipo_operacion"]       = tipoOperacion,
                ["naturaleza_carga"]     = naturalezaCarga,
                ["cantidad"]             = cantidad.ToString(CultureInfo.InvariantCulture),
                ["unidad_medida"]        = unidadMedida,
                ["tipo_empaque"]         = tipoEmpaque,
                ["codigo_producto"]      = codigoProducto,
                ["descripcion_producto"] = descripcionProducto,
                ["generador_tipo_id"]    = generadorTipoId,
                ["generador_nit"]        = generadorNit,
                ["generador_sede"]       = generadorSede,
                ["remitente_nit"]        = remitenteNit,
                ["remitente_sede"]       = remitenteSede,
                ["remitente_municipio_dane"] = remitenteMunicipioDane,
                ["propietario_tipo_id"]  = propietarioTipoId,
                ["propietario_nit"]      = propietarioNit,
                ["propietario_sede"]     = propietarioSede,
                ["destinatario_tipo_id"] = Or(dto.DestinatarioTipoId, "N"),
                ["destinatario_nit"]     = destinatarioNit,
                ["destinatario_sede"]    = Or(dto.DestinatarioSede, "00"),
                ["destinatario_municipio_dane"] = destinatarioMunicipioDane,
                ["fecha_cita_cargue"]    = fechaCitaCargue,
                ["hora_cita_cargue"]     = Or(dto.HoraCitaCargue, ""),
                ["fecha_cita_descargue"] = fechaCitaDescargue,
                ["hora_cita_descargue"]  = Or(dto.HoraCitaDescargue, ""),
                ["poliza_numero"]        = Or(dto.PolizaNumero, Or(settings.PolizaNumero, "")),
                ["poliza_vencimiento"]   = Or(dto.PolizaVencimiento?.ToString("dd/MM/yyyy"), Or(settings.PolizaVencimiento?.ToString("dd/MM/yyyy"), "")),
                ["poliza_aseguradora"]   = Or(dto.PolizaAseguradora, Or(settings.PolizaAseguradora, "")),
                ["poliza_aseguradora_nit"] = Or(dto.PolizaAseguradoraNit, Or(settings.PolizaAseguradoraNit, "")),
            }
        };

        var xml = _xmlGen.GenerarXmlRemesa(req);

        // Sincronizar el dto con los valores ya resueltos antes de persistir en Supabase
        dto.GeneradorNit = generadorNit;
        dto.GeneradorTipoId = generadorTipoId;
        dto.GeneradorSede = generadorSede;
        dto.RemitenteNit = remitenteNit;
        dto.RemitenteSede = remitenteSede;
        dto.PropietarioNit = propietarioNit;
        dto.PropietarioTipoId = propietarioTipoId;
        dto.PropietarioSede = propietarioSede;
        dto.RemitenteMunicipioDane = remitenteMunicipioDane;
        dto.DestinatarioMunicipioDane = destinatarioMunicipioDane;
        dto.Cantidad = cantidad;
        dto.TipoOperacion = tipoOperacion;
        dto.NaturalezaCarga = naturalezaCarga;
        dto.UnidadMedida = unidadMedida;
        dto.TipoEmpaque = tipoEmpaque;
        dto.CodigoProducto = codigoProducto;
        dto.DescripcionProducto = descripcionProducto;

        var remesaId = await _db.CrearRemesaDraftAsync(dto, consecutivo, xml);
        var resultado = await _rndc.EnviarAsync(xml, 3, simulacion: settings.Simulacion);

        if (resultado.Exito)
        {
            await _db.ActualizarRemesaRndcAsync(remesaId, resultado.Radicado!, xml, resultado.XmlRespuesta!, "generated");
        }
        else
        {
            await _db.ActualizarRemesaRndcAsync(remesaId, null, xml, resultado.XmlRespuesta ?? "", "error_rndc", resultado.Error);
        }

        return Ok(new
        {
            exito           = resultado.Exito,
            remesa_id       = remesaId,
            consecutivo     = consecutivo,
            radicado_rndc   = resultado.Radicado,
            error           = resultado.Error,
            codigo_error    = resultado.CodigoError
        });
    }

    [HttpPost("cumplir")]
    public async Task<IActionResult> Cumplir([FromBody] CumplirRemesaDto dto)
    {
        var remesa = await _db.GetRemesaAsync(dto.RemesaId);
        if (remesa == null) return NotFound(new { error = "Remesa no encontrada" });
        if (remesa.Estado != "generated")
            return BadRequest(new { error = $"La remesa debe estar en estado 'generated'. Estado actual: {remesa.Estado}" });

        var settings = await _db.GetSettingsAsync();

        var req = new RndcRequest
        {
            Username  = dto.RndcUsername,
            Password  = dto.RndcPassword,
            Simulacion = settings.Simulacion,
            ProcesoId = 5,
            Variables = new Dictionary<string, string>
            {
                ["nit_empresa"]        = settings.EmpresaNit,
                ["consecutivo_remesa"] = remesa.Consecutivo,
                ["tipo_cumplido"]      = dto.TipoCumplido,
                ["motivo_suspension"]  = dto.MotivoSuspension ?? "",
                ["consecuencia"]       = dto.Consecuencia ?? "",
                ["observaciones"]      = dto.Observaciones ?? "NINGUNA",
            }
        };

        var xml = _xmlGen.GenerarXmlCumplirRemesa(req);
        var resultado = await _rndc.EnviarAsync(xml, 5, simulacion: settings.Simulacion);

        var nuevoEstado = resultado.Exito ? "cumplida" : "error_rndc";
        await _db.ActualizarRemesaRndcAsync(
            dto.RemesaId, resultado.Radicado, xml,
            resultado.XmlRespuesta ?? "", nuevoEstado, resultado.Error);

        return Ok(new {
            exito        = resultado.Exito,
            radicado     = resultado.Radicado,
            error        = resultado.Error,
            codigo_error = resultado.CodigoError
        });
    }

    private static object ToResponse(Remesa r) => new
    {
        id = r.Id, consecutivo = r.Consecutivo,
        generador_tipo_id = r.GeneradorTipoId, generador_nit = r.GeneradorNit,
        generador_dv = r.GeneradorDv, generador_nombre = r.GeneradorNombre, generador_sede = r.GeneradorSede,
        remitente_tipo_id = r.RemitenteTipoId, remitente_nit = r.RemitenteNit,
        remitente_nombre = r.RemitenteNombre, remitente_sede = r.RemitenteSede,
        remitente_direccion = r.RemitenteDireccion, remitente_municipio_dane = r.RemitenteMunicipioDane,
        destinatario_tipo_id = r.DestinatarioTipoId, destinatario_nit = r.DestinatarioNit,
        destinatario_nombre = r.DestinatarioNombre, destinatario_sede = r.DestinatarioSede,
        destinatario_direccion = r.DestinatarioDireccion, destinatario_municipio_dane = r.DestinatarioMunicipioDane,
        naturaleza_carga = r.NaturalezaCarga, codigo_producto = r.CodigoProducto,
        descripcion_producto = r.DescripcionProducto, tipo_empaque = r.TipoEmpaque,
        cantidad = r.Cantidad, unidad_medida = r.UnidadMedida, peso_kg = r.PesoKg,
        tipo_operacion = r.TipoOperacion, valor_mercancia = r.ValorMercancia, valor_flete = r.ValorFlete,
        poliza_numero = r.PolizaNumero, poliza_vencimiento = r.PolizaVencimiento,
        poliza_aseguradora = r.PolizaAseguradora, poliza_aseguradora_nit = r.PolizaAseguradoraNit,
        raw_message = r.RawMessage, cliente_nombre = r.ClienteNombre, obra = r.Obra,
        programa = r.Programa, observaciones = r.Observaciones,
        radicado_rndc = r.RadicadoRndc, estado = r.Estado, error_detalle = r.ErrorDetalle,
        created_by = r.CreatedBy, created_at = r.CreatedAt, updated_at = r.UpdatedAt
    };

    private static object ToManifiestoResponse(Manifiesto m) => new
    {
        id = m.Id, consecutivo = m.Consecutivo, remesa_id = m.RemesaId,
        placa_vehiculo = m.PlacaVehiculo, placa_remolque = m.PlacaRemolque,
        conductor_tipo_id = m.ConductorTipoId, conductor_cedula = m.ConductorCedula,
        conductor_nombre = m.ConductorNombre, conductor2_cedula = m.Conductor2Cedula,
        conductor2_nombre = m.Conductor2Nombre, propietario_tipo_id = m.PropietarioTipoId,
        propietario_cedula = m.PropietarioCedula, propietario_nombre = m.PropietarioNombre,
        fecha_despacho = m.FechaDespacho, fecha_limite_entrega = m.FechaLimiteEntrega,
        tipo_valor_pactado = m.TipoValorPactado, valor_viaje = m.ValorViaje,
        valor_anticipo = m.ValorAnticipo, municipio_pago_dane = m.MunicipioPagoDane,
        fecha_limite_pago = m.FechaLimitePago, resp_cargue = m.RespCargue,
        resp_descargue = m.RespDescargue, horas_espera_cargue = m.HorasEsperaCargue,
        horas_espera_descargue = m.HorasEsperaDescargue, observaciones = m.Observaciones,
        radicado_rndc = m.RadicadoRndc, numero_autorizacion = m.NumeroAutorizacion,
        pdf_url = m.PdfUrl, estado = m.Estado, error_detalle = m.ErrorDetalle,
        created_by = m.CreatedBy, created_at = m.CreatedAt, updated_at = m.UpdatedAt
    };
}
