namespace TransportesGutierrez.Api.Dtos;

public class CumplirRemesaDto
{
    public string RndcUsername { get; set; } = "";
    public string RndcPassword { get; set; } = "";
    public string RemesaId { get; set; } = "";
    public string TipoCumplido { get; set; } = "N";
    public string? MotivoSuspension { get; set; }
    public string? Consecuencia { get; set; }
    public string? Observaciones { get; set; }
}
