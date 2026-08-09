namespace TransportesGutierrez.Api.Models;

public class Manifiesto
{
    public string Id { get; set; } = "";
    public string Consecutivo { get; set; } = "";
    public string RemesaId { get; set; } = "";  // FK → remesas.id

    // Vehículo
    public string PlacaVehiculo { get; set; } = "";
    public string? PlacaRemolque { get; set; }

    // Conductor principal
    public string ConductorTipoId { get; set; } = "C";
    public string ConductorCedula { get; set; } = "";
    public string? ConductorNombre { get; set; }

    // Segundo conductor (viajes largos)
    public string? Conductor2Cedula { get; set; }
    public string? Conductor2Nombre { get; set; }

    // Propietario vehículo
    public string? PropietarioTipoId { get; set; }
    public string? PropietarioCedula { get; set; }
    public string? PropietarioNombre { get; set; }

    // Programación
    public DateTime? FechaDespacho { get; set; }
    public DateTime? FechaLimiteEntrega { get; set; }

    // Económico
    public string TipoValorPactado { get; set; } = "B"; // K, G, B
    public decimal ValorViaje { get; set; }
    public decimal ValorAnticipo { get; set; }
    public decimal ValorSaldo => ValorViaje - ValorAnticipo;

    // Pago saldo
    public string? MunicipioPagoDane { get; set; }
    public DateTime? FechaLimitePago { get; set; }

    // Responsables
    public string RespCargue { get; set; } = "D";    // D, E, G
    public string RespDescargue { get; set; } = "D";

    // Horas espera
    public int HorasEsperaCargue { get; set; }
    public int HorasEsperaDescargue { get; set; }

    public string? Observaciones { get; set; }

    // RNDC
    public string? RadicadoRndc { get; set; }
    public string? NumeroAutorizacion { get; set; }
    public string? XmlEnviado { get; set; }
    public string? XmlRespuesta { get; set; }
    public string? PdfUrl { get; set; }

    // Estado
    public string Estado { get; set; } = "draft";
    // draft | pending_rndc | sent_rndc | generated | error_rndc | cumplido | anulado
    public string? ErrorDetalle { get; set; }

    // Auditoría
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
