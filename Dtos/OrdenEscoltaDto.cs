namespace TransportesGutierrez.Api.Dtos;

public sealed class CrearOrdenEscoltaDto
{
    public DateOnly Fecha { get; set; }
    public string Empresa { get; set; } = "";
    public string PlacaCamabaja { get; set; } = "";
    public string? PlacaEscolta { get; set; }
    public string? NombreEscolta { get; set; }
    public string? Observaciones { get; set; }
    public List<OrdenEscoltaItemDto> Viajes { get; set; } = new();
    public string? ClienteId { get; set; }
    public string? ClienteDocumentoSnapshot { get; set; }
    public string? VehiculoPlacaSnapshot { get; set; }
}

public sealed class OrdenEscoltaItemDto
{
    public string Maquina { get; set; } = "";
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
}

public sealed class EnviarOrdenEscoltaDto
{
    public string PdfBase64 { get; set; } = "";
}
