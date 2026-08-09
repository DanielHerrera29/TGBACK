namespace TransportesGutierrez.Api.Models;

public class Remesa
{
    public string Id { get; set; } = "";
    public string Consecutivo { get; set; } = "";

    // Generador
    public string GeneradorTipoId { get; set; } = "N";
    public string GeneradorNit { get; set; } = "";
    public string? GeneradorDv { get; set; }
    public string? GeneradorNombre { get; set; }
    public string GeneradorSede { get; set; } = "00";

    // Remitente (cargue)
    public string RemitenteTipoId { get; set; } = "N";
    public string RemitenteNit { get; set; } = "";
    public string? RemitenteNombre { get; set; }
    public string RemitenteSede { get; set; } = "00";
    public string? RemitenteDireccion { get; set; }
    public string RemitenteMunicipioDane { get; set; } = "11001000";

    // Destinatario (descargue)
    public string DestinatarioTipoId { get; set; } = "N";
    public string DestinatarioNit { get; set; } = "";
    public string? DestinatarioNombre { get; set; }
    public string DestinatarioSede { get; set; } = "00";
    public string? DestinatarioDireccion { get; set; }
    public string DestinatarioMunicipioDane { get; set; } = "11001000";

    // Mercancía
    public string NaturalezaCarga { get; set; } = "N";
    public string? CodigoProducto { get; set; }
    public string? DescripcionProducto { get; set; }
    public string? TipoEmpaque { get; set; }
    public double? Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "KG";
    public double PesoKg { get; set; }

    // Operación
    public string TipoOperacion { get; set; } = "N";
    public double ValorMercancia { get; set; }
    public double ValorFlete { get; set; }

    // Póliza
    public string? PolizaNumero { get; set; }
    public DateTime? PolizaVencimiento { get; set; }
    public string? PolizaAseguradora { get; set; }
    public string? PolizaAseguradoraNit { get; set; }

    // WhatsApp
    public string? RawMessage { get; set; }
    public string? ClienteNombre { get; set; }
    public string? Obra { get; set; }
    public string? Programa { get; set; }
    public string? Observaciones { get; set; }

    // RNDC
    public string? RadicadoRndc { get; set; }
    public string? XmlEnviado { get; set; }
    public string? XmlRespuesta { get; set; }

    // Estado
    public string Estado { get; set; } = "draft";
    // draft | pending_rndc | sent_rndc | generated | error_rndc | anulada
    public string? ErrorDetalle { get; set; }

    // Auditoría
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
