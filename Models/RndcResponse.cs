namespace TransportesGutierrez.Api.Models;

public class RndcResponse
{
    public bool Exito { get; set; }
    public string? Radicado { get; set; }
    public string? NumeroAutorizacion { get; set; }
    public string? XmlEnviado { get; set; }
    public string? XmlRespuesta { get; set; }
    public string? Error { get; set; }
    public string? CodigoError { get; set; }
}

public class RndcRequest
{
    public string Username { get; set; } = "";   // email usuario app = user RNDC
    public string Password { get; set; } = "";   // password app = password RNDC
    public string Simulacion { get; set; } = "S";
    public int ProcesoId { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
}
