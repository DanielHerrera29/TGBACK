using Microsoft.AspNetCore.Mvc;
using TransportesGutierrez.Api.Services;
using TransportesGutierrez.Api.Dtos;
using TransportesGutierrez.Api.Models;

namespace TransportesGutierrez.Api.Controllers;

[ApiController]
[Route("api/rndc")]
public class RndcController : ControllerBase
{
    private readonly SupabaseService _supabase;
    private readonly RndcClient _rndc;
    private readonly XmlGeneratorService _xml;
    private readonly ILogger<RndcController> _logger;

    public RndcController(
        SupabaseService supabase,
        RndcClient rndc,
        XmlGeneratorService xml,
        ILogger<RndcController> logger)
    {
        _supabase = supabase;
        _rndc = rndc;
        _xml = xml;
        _logger = logger;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            status = "ok",
            service = "RNDC API"
        });
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var supabaseAvailable = await _supabase.IsAvailableAsync(cancellationToken);
        var result = new
        {
            status = supabaseAvailable ? "healthy" : "degraded",
            supabase = supabaseAvailable ? "connected" : "unavailable"
        };

        return supabaseAvailable ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }

    [HttpPost("vehiculos/maestro")]
    public IActionResult MaestroVehiculos()
    {
        // Cat\u00e1logos visibles de respaldo. El RNDC no expone una consulta p\u00fablica
        // estable para estos valores desde el WS; el alta final siempre es validada por proceso 12.
        return Ok(new
        {
            configuraciones_unidad_carga = new[]
            {
                new { codigo = "50", nombre = "Camion rigido de 2 ejes" },
                new { codigo = "53", nombre = "Tractocamion de 2 ejes" },
                new { codigo = "54", nombre = "Tractocamion de 3 ejes" },
                new { codigo = "55", nombre = "Tractocamion de mas de 3 ejes" },
                new { codigo = "56", nombre = "Camion rigido de mas de 4 ejes" },
                new { codigo = "63", nombre = "Semirremolque de 3 ejes" },
                new { codigo = "64", nombre = "Semirremolque de mas de 3 ejes" },
                new { codigo = "74", nombre = "Remolque de mas de 4 ejes" },
                new { codigo = "85", nombre = "Remolque balanceado de mas de 4 ejes" },
            },
            tipos_carroceria = new[]
            {
                new { codigo = "0", nombre = "S.R.S. (sin remolque o semirremolque)" },
                new { codigo = "1", nombre = "Estacas" },
            },
            tipos_identificacion = new[]
            {
                new { codigo = "C", nombre = "Cedula de ciudadania" },
                new { codigo = "N", nombre = "NIT" },
                new { codigo = "T", nombre = "Tarjeta de identidad" },
                new { codigo = "E", nombre = "Cedula de extranjeria" },
                new { codigo = "P", nombre = "Pasaporte" },
                new { codigo = "U", nombre = "NUIP" },
            },
        });
    }

    [HttpPost("vehiculos")]
    public async Task<IActionResult> RegistrarVehiculo(
        [FromBody] RegistrarVehiculoDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Solicitud de vehiculo RNDC: placa={Placa}, configuracion={Configuracion}, carroceria={Carroceria}, tipoTenedor={TipoTenedor}, tieneIdTenedor={TieneIdTenedor}",
            dto.NumPlaca,
            dto.CodConfiguracionUnidadCarga,
            dto.CodTipoCarroceria,
            dto.CodTipoIdTenedor,
            !string.IsNullOrWhiteSpace(dto.NumIdTenedor));

        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.RndcUsername)) faltantes.Add("usuario RNDC");
        if (string.IsNullOrWhiteSpace(dto.RndcPassword)) faltantes.Add("clave RNDC");
        if (string.IsNullOrWhiteSpace(dto.NumPlaca)) faltantes.Add("placa");
        if (string.IsNullOrWhiteSpace(dto.CodConfiguracionUnidadCarga)) faltantes.Add("configuracion de unidad de carga");
        if (dto.PesoVehiculoVacio <= 0) faltantes.Add("peso vacio");
        if (string.IsNullOrWhiteSpace(dto.CodTipoCarroceria)) faltantes.Add("tipo de carroceria");
        if (string.IsNullOrWhiteSpace(dto.CodTipoIdTenedor)) faltantes.Add("tipo de identificacion del tenedor");
        if (string.IsNullOrWhiteSpace(dto.NumIdTenedor)) faltantes.Add("identificacion del tenedor");
        if (faltantes.Count > 0)
            return BadRequest(new { exito = false, error = $"Faltan datos para registrar el vehiculo: {string.Join(", ", faltantes)}." });

        var settings = await _supabase.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.EmpresaNit))
            return BadRequest(new { exito = false, error = "Falta el NIT de la empresa en Configuracion." });

        var request = new RndcRequest
        {
            Username = dto.RndcUsername.Trim(),
            Password = dto.RndcPassword,
            Simulacion = settings.Simulacion,
            ProcesoId = 12,
            Variables = new Dictionary<string, string>
            {
                ["nit_empresa"] = settings.EmpresaNit,
                ["num_placa"] = dto.NumPlaca.Trim().ToUpperInvariant(),
                ["configuracion"] = dto.CodConfiguracionUnidadCarga.Trim(),
                ["peso_vacio"] = dto.PesoVehiculoVacio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["carroceria"] = dto.CodTipoCarroceria.Trim(),
                ["tipo_id_tenedor"] = dto.CodTipoIdTenedor.Trim().ToUpperInvariant(),
                ["num_id_tenedor"] = dto.NumIdTenedor.Trim(),
            },
        };

        var response = await _rndc.EnviarAsync(_xml.GenerarXmlVehiculo(request), 12, 1, settings.Simulacion);
        return Ok(new
        {
            exito = response.Exito,
            vehiculo_id = response.Radicado,
            error = response.Error,
        });
    }
}
