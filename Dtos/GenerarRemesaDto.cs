namespace TransportesGutierrez.Api.Dtos;

public class GenerarRemesaDto
{
    public string RndcUsername { get; set; } = "";
    public string RndcPassword { get; set; } = "";

    public string? TipoOperacion { get; set; }
    public string? NaturalezaCarga { get; set; }
    public double? Cantidad { get; set; }
    public string? UnidadMedida { get; set; }
    public string? TipoEmpaque { get; set; }
    public double PesoKg { get; set; }
    public string? CodigoProducto { get; set; }
    public string? DescripcionProducto { get; set; }

    public string? GeneradorTipoId { get; set; }
    public string? GeneradorNit { get; set; }
    public string? GeneradorSede { get; set; }

    public string? RemitenteTipoId { get; set; }
    public string? RemitenteNit { get; set; }
    public string? RemitenteSede { get; set; }

    public string? PropietarioTipoId { get; set; }
    public string? PropietarioNit { get; set; }
    public string? PropietarioSede { get; set; }

    public string? DestinatarioTipoId { get; set; }
    public string? DestinatarioNit { get; set; }
    public string? DestinatarioSede { get; set; }

    public string? RemitenteMunicipioDane { get; set; }
    public string? DestinatarioMunicipioDane { get; set; }

    public string? PolizaNumero { get; set; }
    public DateTime? PolizaVencimiento { get; set; }
    public string? PolizaAseguradora { get; set; }
    public string? PolizaAseguradoraNit { get; set; }

    public string? FechaCitaCargue { get; set; }
    public string? HoraCitaCargue { get; set; }
    public string? FechaCitaDescargue { get; set; }
    public string? HoraCitaDescargue { get; set; }

    public string? RawMessage { get; set; }
    public string? ClienteNombre { get; set; }
    public string? Obra { get; set; }
    public string? Programa { get; set; }
    public string? Observaciones { get; set; }
}
