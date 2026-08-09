namespace TransportesGutierrez.Api.Dtos;

public class CumplirManifiestoDto
{
    public string RndcUsername { get; set; } = "";
    public string RndcPassword { get; set; } = "";
    public string ManifiestoId { get; set; } = "";
    public string TipoCumplido { get; set; } = "N";
    public string? MotivoSuspension { get; set; }
    public string? Consecuencia { get; set; }
    public decimal ValorAdicionalCargue { get; set; }
    public decimal ValorAdicionalDescargue { get; set; }
    public decimal ValorAdicional { get; set; }
    public string? MotivoValorAdicional { get; set; }
    public decimal ValorDescuento { get; set; }
    public string? MotivoDescuento { get; set; }
    public decimal ValorSobreanticipo { get; set; }
    public string? Observaciones { get; set; }
}
