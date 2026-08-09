using Microsoft.AspNetCore.Mvc;
using TransportesGutierrez.Api.Dtos;
using TransportesGutierrez.Api.Models;
using TransportesGutierrez.Api.Services;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/manifiesto")]
public class ManifiestoController : ControllerBase
{
    private readonly XmlGeneratorService _xmlGen;
    private readonly RndcClient _rndc;
    private readonly SupabaseService _db;

    public ManifiestoController(XmlGeneratorService xmlGen, RndcClient rndc, SupabaseService db)
    {
        _xmlGen = xmlGen;
        _rndc = rndc;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var manifiestos = await _db.GetManifiestosAsync();
        var remesas = await _db.GetRemesasAsync();
        var consecutivos = remesas.ToDictionary(r => r.Id, r => r.Consecutivo);

        return Ok(manifiestos.Select(m =>
        {
            consecutivos.TryGetValue(m.RemesaId, out var remesaConsecutivo);
            return new
            {
                manifiesto = ToResponse(m),
                remesa_consecutivo = remesaConsecutivo
            };
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Obtener(string id)
    {
        var manifiesto = await _db.GetManifiestoAsync(id);
        if (manifiesto == null) return NotFound(new { error = "Manifiesto no encontrado" });
        var remesa = await _db.GetRemesaAsync(manifiesto.RemesaId);

        return Ok(new
        {
            manifiesto = ToResponse(manifiesto),
            remesa = remesa == null ? null : RemesaControllerResponse(remesa)
        });
    }

    [HttpPost("generar")]
    public async Task<IActionResult> Generar([FromBody] GenerarManifiestoDto dto)
    {
        var remesa = await _db.GetRemesaAsync(dto.RemesaId);
        if (remesa == null) return NotFound(new { error = "Remesa no encontrada" });
        if (remesa.Estado != "generated")
            return BadRequest(new { error = "La remesa debe estar generada en RNDC antes de crear el manifiesto" });

        var settings = await _db.GetSettingsAsync();
        var consecutivo = await _db.GenerarConsecutivoManifiestoAsync();

        var req = new RndcRequest
        {
            Username = dto.RndcUsername,
            Password = dto.RndcPassword,
            Simulacion = settings.Simulacion,
            ProcesoId = 4,
            Variables = new Dictionary<string, string>
            {
                ["nit_empresa"]             = settings.EmpresaNit,
                ["consecutivo"]             = consecutivo,
                ["consecutivo_remesa"]      = remesa.Consecutivo,
                ["municipio_origen_dane"]   = remesa.RemitenteMunicipioDane,
                ["municipio_destino_dane"]  = remesa.DestinatarioMunicipioDane,
                ["placa_vehiculo"]          = dto.PlacaVehiculo,
                ["placa_remolque"]          = dto.PlacaRemolque ?? "",
                ["conductor_tipo_id"]       = dto.ConductorTipoId ?? "C",
                ["conductor_cedula"]        = dto.ConductorCedula,
                ["conductor2_cedula"]       = dto.Conductor2Cedula ?? "",
                ["propietario_tipo_id"]     = dto.PropietarioTipoId ?? "C",
                ["propietario_cedula"]      = dto.PropietarioCedula ?? "",
                ["fecha_despacho"]          = dto.FechaDespacho?.ToString("dd/MM/yyyy") ?? "",
                ["fecha_limite_entrega"]    = dto.FechaLimiteEntrega?.ToString("dd/MM/yyyy HH:mm") ?? "",
                ["tipo_valor_pactado"]      = dto.TipoValorPactado ?? "B",
                ["valor_viaje"]             = dto.ValorViaje.ToString(),
                ["valor_anticipo"]          = dto.ValorAnticipo.ToString(),
                ["municipio_pago_dane"]     = dto.MunicipioPagoDane ?? "",
                ["fecha_limite_pago"]       = dto.FechaLimitePago?.ToString("dd/MM/yyyy") ?? "",
                ["resp_cargue"]             = dto.RespCargue ?? "D",
                ["resp_descargue"]          = dto.RespDescargue ?? "D",
                ["horas_espera_cargue"]     = dto.HorasEsperaCargue.ToString(),
                ["horas_espera_descargue"]  = dto.HorasEsperaDescargue.ToString(),
                ["observaciones"]           = dto.Observaciones ?? "NINGUNA",
            }
        };

        var xml = _xmlGen.GenerarXmlManifiesto(req);
        var manifiestoId = await _db.CrearManifiestoDraftAsync(dto, remesa.Id, consecutivo, xml);

        if (string.IsNullOrEmpty(manifiestoId))
        {
            return BadRequest(new
            {
                exito = false,
                error = "Error guardando manifiesto en Supabase. Ver logs del servidor."
            });
        }

        var resultado = await _rndc.EnviarAsync(xml, 4, simulacion: settings.Simulacion);

        if (resultado.Exito)
        {
            await _db.ActualizarManifiestoRndcAsync(manifiestoId, resultado.Radicado!, xml, resultado.XmlRespuesta!, "generated");
        }
        else
        {
            await _db.ActualizarManifiestoRndcAsync(manifiestoId, null, xml, resultado.XmlRespuesta ?? "", "error_rndc", resultado.Error);
        }

        return Ok(new
        {
            exito                = resultado.Exito,
            manifiesto_id        = manifiestoId,
            consecutivo          = consecutivo,
            numero_autorizacion  = resultado.Radicado,
            error                = resultado.Error,
            codigo_error         = resultado.CodigoError
        });
    }

    [HttpPost("cumplir")]
    public async Task<IActionResult> Cumplir([FromBody] CumplirManifiestoDto dto)
    {
        var manifiesto = await _db.GetManifiestoAsync(dto.ManifiestoId);
        if (manifiesto == null) return NotFound(new { error = "Manifiesto no encontrado" });
        if (manifiesto.Estado != "generated")
            return BadRequest(new { error = $"El manifiesto debe estar en estado 'generated'. Estado actual: {manifiesto.Estado}" });

        var settings = await _db.GetSettingsAsync();

        var req = new RndcRequest
        {
            Username   = dto.RndcUsername,
            Password   = dto.RndcPassword,
            Simulacion = settings.Simulacion,
            ProcesoId  = 6,
            Variables  = new Dictionary<string, string>
            {
                ["nit_empresa"]             = settings.EmpresaNit,
                ["consecutivo_manifiesto"]  = manifiesto.Consecutivo,
                ["tipo_cumplido"]           = dto.TipoCumplido,
                ["motivo_suspension"]       = dto.MotivoSuspension ?? "",
                ["consecuencia"]            = dto.Consecuencia ?? "",
                ["valor_adicional_cargue"]  = dto.ValorAdicionalCargue.ToString(),
                ["valor_adicional_descargue"] = dto.ValorAdicionalDescargue.ToString(),
                ["valor_adicional"]         = dto.ValorAdicional.ToString(),
                ["motivo_valor_adicional"]  = dto.MotivoValorAdicional ?? "",
                ["valor_descuento"]         = dto.ValorDescuento.ToString(),
                ["motivo_descuento"]        = dto.MotivoDescuento ?? "",
                ["valor_sobreanticipo"]     = dto.ValorSobreanticipo.ToString(),
                ["observaciones"]           = dto.Observaciones ?? "NINGUNA",
            }
        };

        var xml = _xmlGen.GenerarXmlCumplirManifiesto(req);
        var resultado = await _rndc.EnviarAsync(xml, 6, simulacion: settings.Simulacion);

        var nuevoEstado = resultado.Exito ? "completado" : "error_rndc";
        await _db.ActualizarManifiestoRndcAsync(
            dto.ManifiestoId, resultado.Radicado, xml,
            resultado.XmlRespuesta ?? "", nuevoEstado, resultado.Error);

        return Ok(new {
            exito        = resultado.Exito,
            radicado     = resultado.Radicado,
            error        = resultado.Error,
            codigo_error = resultado.CodigoError
        });
    }

    private static object ToResponse(Manifiesto m) => new
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

    private static object RemesaControllerResponse(Remesa r) => new
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
}
