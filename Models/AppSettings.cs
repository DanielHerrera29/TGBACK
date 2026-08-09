namespace TransportesGutierrez.Api.Models;

public class AppSettings
{
    public string Id { get; set; } = "";
    public string? EmpresaNombre { get; set; }
    public string EmpresaNit { get; set; } = "";
    public string? EmpresaDv { get; set; }
    public string EmpresaMunicipioDane { get; set; } = "11001000";

    // Póliza
    public string? PolizaNumero { get; set; }
    public DateTime? PolizaVencimiento { get; set; }
    public string? PolizaAseguradora { get; set; }
    public string? PolizaAseguradoraNit { get; set; }

    // Generador por defecto
    public string GeneradorTipoId { get; set; } = "N";
    public string? GeneradorNit { get; set; }
    public string? GeneradorDv { get; set; }
    public string? GeneradorNombre { get; set; }
    public string GeneradorSede { get; set; } = "00";

    // Modo RNDC
    public string Simulacion { get; set; } = "S"; // S o R
    public bool EsModoSimulacion => Simulacion == "S";
}
