namespace TransportesGutierrez.Api.Dtos;

public class GenerarManifiestoDto
{
    public string RndcUsername { get; set; } = "";
    public string RndcPassword { get; set; } = "";
    public string RemesaId { get; set; } = "";

    public string PlacaVehiculo { get; set; } = "";
    public string? PlacaRemolque { get; set; }

    public string? ConductorTipoId { get; set; }
    public string ConductorCedula { get; set; } = "";
    public string? Conductor2Cedula { get; set; }

    public string? PropietarioTipoId { get; set; }
    public string? PropietarioCedula { get; set; }

    public DateTime? FechaDespacho { get; set; }
    public DateTime? FechaLimiteEntrega { get; set; }

    public string? TipoValorPactado { get; set; }
    public decimal ValorViaje { get; set; }
    public decimal ValorAnticipo { get; set; }
    public string? MunicipioPagoDane { get; set; }
    public DateTime? FechaLimitePago { get; set; }

    public string? RespCargue { get; set; }
    public string? RespDescargue { get; set; }
    public int HorasEsperaCargue { get; set; }
    public int HorasEsperaDescargue { get; set; }

    public string? Observaciones { get; set; }
}
