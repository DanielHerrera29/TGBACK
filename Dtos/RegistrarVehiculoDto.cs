namespace TransportesGutierrez.Api.Dtos;

public class RegistrarVehiculoDto
{
    public string RndcUsername { get; set; } = "";
    public string RndcPassword { get; set; } = "";
    public string NumPlaca { get; set; } = "";
    public string CodConfiguracionUnidadCarga { get; set; } = "";
    public decimal PesoVehiculoVacio { get; set; }
    public string CodTipoCarroceria { get; set; } = "";
    public string CodTipoIdTenedor { get; set; } = "";
    public string NumIdTenedor { get; set; } = "";
}
