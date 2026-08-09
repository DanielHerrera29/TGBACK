using System.Text;
using TransportesGutierrez.Api.Models;

namespace TransportesGutierrez.Api.Services;

public class XmlGeneratorService
{
    public string GenerarXmlVehiculo(RndcRequest req)
    {
        var v = req.Variables;
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version='1.0' encoding='ISO-8859-1' ?>");
        xml.AppendLine("<root>");
        xml.AppendLine("  <acceso>");
        xml.AppendLine($"    <username>{Escape(req.Username)}</username>");
        xml.AppendLine($"    <password>{Escape(req.Password)}</password>");
        xml.AppendLine($"    <simulacion>{Escape(req.Simulacion)}</simulacion>");
        xml.AppendLine("  </acceso>");
        xml.AppendLine("  <solicitud><tipo>1</tipo><procesoid>12</procesoid></solicitud>");
        xml.AppendLine("  <variables>");
        xml.AppendLine($"    <NUMNITEMPRESATRANSPORTE>{Escape(v.GetV("nit_empresa", ""))}</NUMNITEMPRESATRANSPORTE>");
        xml.AppendLine($"    <NUMPLACA>{Escape(v.GetV("num_placa", ""))}</NUMPLACA>");
        xml.AppendLine($"    <CODCONFIGURACIONUNIDADCARGA>{Escape(v.GetV("configuracion", ""))}</CODCONFIGURACIONUNIDADCARGA>");
        xml.AppendLine($"    <PESOVEHICULOVACIO>{Escape(v.GetV("peso_vacio", ""))}</PESOVEHICULOVACIO>");
        xml.AppendLine($"    <CODTIPOCARROCERIA>{Escape(v.GetV("carroceria", ""))}</CODTIPOCARROCERIA>");
        xml.AppendLine($"    <CODTIPOIDTENEDOR>{Escape(v.GetV("tipo_id_tenedor", ""))}</CODTIPOIDTENEDOR>");
        xml.AppendLine($"    <NUMIDTENEDOR>{Escape(v.GetV("num_id_tenedor", ""))}</NUMIDTENEDOR>");
        xml.AppendLine("  </variables>");
        xml.AppendLine("</root>");
        return xml.ToString();
    }

    public string GenerarXmlRemesa(RndcRequest req)
    {
        var v = req.Variables;
        var hoy = DateTime.Now;

        var xml = new StringBuilder();
        xml.AppendLine("<?xml version='1.0' encoding='ISO-8859-1' ?>");
        xml.AppendLine("<root>");

        xml.AppendLine("  <acceso>");
        xml.AppendLine($"    <username>{Escape(req.Username)}</username>");
        xml.AppendLine($"    <password>{Escape(req.Password)}</password>");
        xml.AppendLine($"    <simulacion>{Escape(req.Simulacion)}</simulacion>");
        xml.AppendLine("  </acceso>");

        xml.AppendLine("  <solicitud>");
        xml.AppendLine("    <tipo>1</tipo>");
        xml.AppendLine($"    <procesoid>{req.ProcesoId}</procesoid>");
        xml.AppendLine("  </solicitud>");

        xml.AppendLine("  <variables>");

        xml.AppendLine($"    <NUMNITEMPRESATRANSPORTE>{Escape(v.GetV("nit_empresa", ""))}</NUMNITEMPRESATRANSPORTE>");
        xml.AppendLine($"    <CONSECUTIVOREMESA>{Escape(v.GetV("consecutivo", ""))}</CONSECUTIVOREMESA>");
        xml.AppendLine($"    <CONSECUTIVOINFORMACIONCARGA></CONSECUTIVOINFORMACIONCARGA>");

        xml.AppendLine($"    <CODOPERACIONTRANSPORTE>{Escape(v.GetV("tipo_operacion", "G"))}</CODOPERACIONTRANSPORTE>");
        xml.AppendLine($"    <CODNATURALEZACARGA>{Escape(v.GetV("naturaleza_carga", "1"))}</CODNATURALEZACARGA>");
        xml.AppendLine($"    <CANTIDADCARGADA>{Escape(v.GetV("cantidad", "0"))}</CANTIDADCARGADA>");
        xml.AppendLine($"    <UNIDADMEDIDACAPACIDAD>{Escape(v.GetV("unidad_medida", "1"))}</UNIDADMEDIDACAPACIDAD>");
        xml.AppendLine($"    <CODTIPOEMPAQUE>{Escape(v.GetV("tipo_empaque", "4"))}</CODTIPOEMPAQUE>");
        xml.AppendLine($"    <PESOCONTENEDORVACIO>0</PESOCONTENEDORVACIO>");
        xml.AppendLine($"    <MERCANCIAREMESA>{Escape(v.GetV("codigo_producto", ""))}</MERCANCIAREMESA>");
        xml.AppendLine($"    <DESCRIPCIONCORTAPRODUCTO>{Escape(v.GetV("descripcion_producto", ""))}</DESCRIPCIONCORTAPRODUCTO>");

        // Remitente (cargue) — usa remitente_nit con fallback a generador_nit
        xml.AppendLine($"    <CODTIPOIDREMITENTE>{Escape(v.GetV("remitente_tipo_id", v.GetV("generador_tipo_id", "N")))}</CODTIPOIDREMITENTE>");
        xml.AppendLine($"    <NUMIDREMITENTE>{Escape(v.GetV("remitente_nit", v.GetV("generador_nit", "")))}</NUMIDREMITENTE>");
        xml.AppendLine($"    <CODSEDEREMITENTE>{Escape(v.GetV("remitente_sede", v.GetV("generador_sede", "00")))}</CODSEDEREMITENTE>");

        // Destinatario (descargue)
        xml.AppendLine($"    <CODTIPOIDDESTINATARIO>{Escape(v.GetV("destinatario_tipo_id", "N"))}</CODTIPOIDDESTINATARIO>");
        xml.AppendLine($"    <NUMIDDESTINATARIO>{Escape(v.GetV("destinatario_nit", ""))}</NUMIDDESTINATARIO>");
        xml.AppendLine($"    <CODSEDEDESTINATARIO>{Escape(v.GetV("destinatario_sede", "00"))}</CODSEDEDESTINATARIO>");

        // Póliza
        var duenoPoliza = !string.IsNullOrEmpty(v.GetV("poliza_numero")) ? "S" : "N";
        xml.AppendLine($"    <DUENOPOLIZA>{duenoPoliza}</DUENOPOLIZA>");
        xml.AppendLine($"    <NUMPOLIZATRANSPORTE>{Escape(v.GetV("poliza_numero", ""))}</NUMPOLIZATRANSPORTE>");
        xml.AppendLine($"    <COMPANIASEGURO>{Escape(v.GetV("poliza_aseguradora", ""))}</COMPANIASEGURO>");
        xml.AppendLine($"    <FECHAVENCIMIENTOPOLIZACARGA>{Escape(v.GetV("poliza_vencimiento", ""))}</FECHAVENCIMIENTOPOLIZACARGA>");

        // Horas pactadas
        xml.AppendLine($"    <HORASPACTOCARGA>1</HORASPACTOCARGA>");
        xml.AppendLine($"    <MINUTOSPACTOCARGA>0</MINUTOSPACTOCARGA>");
        xml.AppendLine($"    <HORASPACTODESCARGUE>1</HORASPACTODESCARGUE>");
        xml.AppendLine($"    <MINUTOSPACTODESCARGUE>0</MINUTOSPACTODESCARGUE>");

        // Propietario — por defecto = generador (la empresa transportista es dueña del vehículo)
        xml.AppendLine($"    <CODTIPOIDPROPIETARIO>{Escape(v.GetV("propietario_tipo_id", v.GetV("generador_tipo_id", "N")))}</CODTIPOIDPROPIETARIO>");
        xml.AppendLine($"    <NUMIDPROPIETARIO>{Escape(v.GetV("propietario_nit", v.GetV("generador_nit", "")))}</NUMIDPROPIETARIO>");
        xml.AppendLine($"    <CODSEDEPROPIETARIO>{Escape(v.GetV("propietario_sede", v.GetV("generador_sede", "00")))}</CODSEDEPROPIETARIO>");

        // Fechas de cita (obligatorias)
        var fechaCargue = v.GetV("fecha_cita_cargue", hoy.ToString("dd/MM/yyyy"));
        var horaCargue = v.GetV("hora_cita_cargue", "08:00");
        var fechaDescargue = v.GetV("fecha_cita_descargue", hoy.AddDays(1).ToString("dd/MM/yyyy"));
        var horaDescargue = v.GetV("hora_cita_descargue", "14:00");
        xml.AppendLine($"    <ORDENSERVICIOGENERADOR></ORDENSERVICIOGENERADOR>");
        xml.AppendLine($"    <FECHACITAPACTADACARGUE>{Escape(fechaCargue)}</FECHACITAPACTADACARGUE>");
        xml.AppendLine($"    <HORACITAPACTADACARGUE>{Escape(horaCargue)}</HORACITAPACTADACARGUE>");
        xml.AppendLine($"    <FECHACITAPACTADADESCARGUE>{Escape(fechaDescargue)}</FECHACITAPACTADADESCARGUE>");
        xml.AppendLine($"    <HORACITAPACTADADESCARGUEREMESA>{Escape(horaDescargue)}</HORACITAPACTADADESCARGUEREMESA>");

        // Campos opcionales
        xml.AppendLine($"    <PERMISOCARGAEXTRA></PERMISOCARGAEXTRA>");
        xml.AppendLine($"    <NUMIDGPS></NUMIDGPS>");
        xml.AppendLine($"    <CODIGOUN></CODIGOUN>");
        xml.AppendLine($"    <SUBPARTIDA_CODE></SUBPARTIDA_CODE>");
        xml.AppendLine($"    <CODIGOARANCEL_CODE></CODIGOARANCEL_CODE>");
        xml.AppendLine($"    <GRUPOEMBALAJEENVASE></GRUPOEMBALAJEENVASE>");
        xml.AppendLine($"    <ESTADOMERCANCIA></ESTADOMERCANCIA>");
        xml.AppendLine($"    <UNIDADMEDIDAPRODUCTO></UNIDADMEDIDAPRODUCTO>");
        xml.AppendLine($"    <CANTIDADPRODUCTO></CANTIDADPRODUCTO>");
        xml.AppendLine($"    <TIPOCONSOLIDADA></TIPOCONSOLIDADA>");
        xml.AppendLine($"    <SERIALCONTENEDOR></SERIALCONTENEDOR>");

        xml.AppendLine("  </variables>");
        xml.AppendLine("</root>");

        return xml.ToString();
    }

    public string GenerarXmlManifiesto(RndcRequest req)
    {
        var v = req.Variables;

        var xml = new StringBuilder();
        xml.AppendLine("<?xml version='1.0' encoding='ISO-8859-1' ?>");
        xml.AppendLine("<root>");

        xml.AppendLine("  <acceso>");
        xml.AppendLine($"    <username>{Escape(req.Username)}</username>");
        xml.AppendLine($"    <password>{Escape(req.Password)}</password>");
        xml.AppendLine($"    <simulacion>{Escape(req.Simulacion)}</simulacion>");
        xml.AppendLine("  </acceso>");

        xml.AppendLine("  <solicitud>");
        xml.AppendLine("    <tipo>1</tipo>");
        xml.AppendLine($"    <procesoid>{req.ProcesoId}</procesoid>");
        xml.AppendLine("  </solicitud>");

        xml.AppendLine("  <variables>");

        xml.AppendLine($"    <NUMNITEMPRESATRANSPORTE>{v.GetV("nit_empresa", "")}</NUMNITEMPRESATRANSPORTE>");
        xml.AppendLine($"    <NUMMANIFIESTOCARGA>{v.GetV("consecutivo", "")}</NUMMANIFIESTOCARGA>");
        xml.AppendLine($"    <CODOPERACIONTRANSPORTE>{v.GetV("tipo_operacion_manifiesto", "G")}</CODOPERACIONTRANSPORTE>");

        xml.AppendLine($"    <CODMUNICIPIOORIGENMANIFIESTO>{v.GetV("municipio_origen_dane", "")}</CODMUNICIPIOORIGENMANIFIESTO>");
        xml.AppendLine($"    <CODMUNICIPIODESTINOMANIFIESTO>{v.GetV("municipio_destino_dane", "")}</CODMUNICIPIODESTINOMANIFIESTO>");

        xml.AppendLine($"    <FECHAEXPEDICIONMANIFIESTO>{v.GetV("fecha_despacho", DateTime.Now.ToString("dd/MM/yyyy"))}</FECHAEXPEDICIONMANIFIESTO>");
        if (v.ContainsKey("fecha_limite_entrega") && !string.IsNullOrEmpty(v["fecha_limite_entrega"]))
            xml.AppendLine($"    <FECHALIMITEENTREGAMERCANCIA>{v["fecha_limite_entrega"]}</FECHALIMITEENTREGAMERCANCIA>");

        xml.AppendLine($"    <NUMPLACA>{v.GetV("placa_vehiculo", "")}</NUMPLACA>");
        if (v.ContainsKey("placa_remolque") && !string.IsNullOrEmpty(v["placa_remolque"]))
            xml.AppendLine($"    <NUMPLACAREMOLQUE>{v["placa_remolque"]}</NUMPLACAREMOLQUE>");

        xml.AppendLine($"    <CODIDTITULARMANIFIESTO>{v.GetV("propietario_tipo_id", "C")}</CODIDTITULARMANIFIESTO>");
        xml.AppendLine($"    <NUMIDTITULARMANIFIESTO>{v.GetV("propietario_cedula", "")}</NUMIDTITULARMANIFIESTO>");

        xml.AppendLine($"    <CODIDCONDUCTOR>{v.GetV("conductor_tipo_id", "C")}</CODIDCONDUCTOR>");
        xml.AppendLine($"    <NUMIDCONDUCTOR>{v.GetV("conductor_cedula", "")}</NUMIDCONDUCTOR>");

        if (v.ContainsKey("conductor2_cedula") && !string.IsNullOrEmpty(v["conductor2_cedula"]))
        {
            xml.AppendLine($"    <CODIDCONDUCTOR2>C</CODIDCONDUCTOR2>");
            xml.AppendLine($"    <NUMIDCONDUCTOR2>{v["conductor2_cedula"]}</NUMIDCONDUCTOR2>");
        }

        xml.AppendLine($"    <VALORFLETEPACTADOVIAJE>{v.GetV("valor_viaje", "0")}</VALORFLETEPACTADOVIAJE>");
        xml.AppendLine($"    <VALORANTICIPOMANIFIESTO>{v.GetV("valor_anticipo", "0")}</VALORANTICIPOMANIFIESTO>");

        if (v.ContainsKey("fecha_limite_pago") && !string.IsNullOrEmpty(v["fecha_limite_pago"]))
            xml.AppendLine($"    <FECHAPAGOSALDOMANIFIESTO>{v["fecha_limite_pago"]}</FECHAPAGOSALDOMANIFIESTO>");

        xml.AppendLine($"    <CODRESPONSABLEPAGOCARGUE>{v.GetV("resp_cargue", "D")}</CODRESPONSABLEPAGOCARGUE>");
        xml.AppendLine($"    <CODRESPONSABLEPAGODESCARGUE>{v.GetV("resp_descargue", "D")}</CODRESPONSABLEPAGODESCARGUE>");

        xml.AppendLine($"    <OBSERVACIONES>{Escape(v.GetV("observaciones", "NINGUNA"))}</OBSERVACIONES>");

        xml.AppendLine($"    <REMESASMAN procesoid=\"43\">");
        xml.AppendLine($"      <REMESA>");
        xml.AppendLine($"        <CONSECUTIVOREMESA>{v.GetV("consecutivo_remesa", "")}</CONSECUTIVOREMESA>");
        xml.AppendLine($"      </REMESA>");
        xml.AppendLine($"    </REMESASMAN>");

        xml.AppendLine("  </variables>");
        xml.AppendLine("</root>");

        return xml.ToString();
    }

    public string GenerarXmlCumplirRemesa(RndcRequest req)
    {
        var v = req.Variables;
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version='1.0' encoding='ISO-8859-1' ?>");
        xml.AppendLine("<root>");
        xml.AppendLine("  <acceso>");
        xml.AppendLine($"    <username>{Escape(req.Username)}</username>");
        xml.AppendLine($"    <password>{Escape(req.Password)}</password>");
        xml.AppendLine($"    <simulacion>{Escape(req.Simulacion)}</simulacion>");
        xml.AppendLine("  </acceso>");
        xml.AppendLine("  <solicitud>");
        xml.AppendLine("    <tipo>1</tipo>");
        xml.AppendLine($"    <procesoid>{req.ProcesoId}</procesoid>");
        xml.AppendLine("  </solicitud>");
        xml.AppendLine("  <variables>");
        xml.AppendLine($"    <NUMNITEMPRESATRANSPORTE>{v["nit_empresa"]}</NUMNITEMPRESATRANSPORTE>");
        xml.AppendLine($"    <CONSECUTIVOREMESA>{v["consecutivo_remesa"]}</CONSECUTIVOREMESA>");
        xml.AppendLine($"    <TIPOCUMPLIDO>{v.GetV("tipo_cumplido", "N")}</TIPOCUMPLIDO>");
        if (v.GetV("tipo_cumplido") == "S")
        {
            xml.AppendLine($"    <MOTIVOSUSPENSION>{v.GetV("motivo_suspension", "")}</MOTIVOSUSPENSION>");
            xml.AppendLine($"    <CONSECUENCIA>{v.GetV("consecuencia", "")}</CONSECUENCIA>");
        }
        xml.AppendLine($"    <OBSERVACIONES>{Escape(v.GetV("observaciones", "NINGUNA"))}</OBSERVACIONES>");
        xml.AppendLine("  </variables>");
        xml.AppendLine("</root>");
        return xml.ToString();
    }

    public string GenerarXmlCumplirManifiesto(RndcRequest req)
    {
        var v = req.Variables;
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version='1.0' encoding='ISO-8859-1' ?>");
        xml.AppendLine("<root>");
        xml.AppendLine("  <acceso>");
        xml.AppendLine($"    <username>{Escape(req.Username)}</username>");
        xml.AppendLine($"    <password>{Escape(req.Password)}</password>");
        xml.AppendLine($"    <simulacion>{Escape(req.Simulacion)}</simulacion>");
        xml.AppendLine("  </acceso>");
        xml.AppendLine("  <solicitud>");
        xml.AppendLine("    <tipo>1</tipo>");
        xml.AppendLine($"    <procesoid>{req.ProcesoId}</procesoid>");
        xml.AppendLine("  </solicitud>");
        xml.AppendLine("  <variables>");
        xml.AppendLine($"    <NUMNITEMPRESATRANSPORTE>{v["nit_empresa"]}</NUMNITEMPRESATRANSPORTE>");
        xml.AppendLine($"    <CONSECUTIVOMANIFIESTO>{v["consecutivo_manifiesto"]}</CONSECUTIVOMANIFIESTO>");
        xml.AppendLine($"    <TIPOCUMPLIDO>{v.GetV("tipo_cumplido", "N")}</TIPOCUMPLIDO>");
        if (v.GetV("tipo_cumplido") == "S")
        {
            xml.AppendLine($"    <MOTIVOSUSPENSION>{v.GetV("motivo_suspension", "")}</MOTIVOSUSPENSION>");
            xml.AppendLine($"    <CONSECUENCIA>{v.GetV("consecuencia", "")}</CONSECUENCIA>");
        }
        xml.AppendLine($"    <VALORADICIONALCARGUE>{v.GetV("valor_adicional_cargue", "0")}</VALORADICIONALCARGUE>");
        xml.AppendLine($"    <VALORADICIONALDESCARGUE>{v.GetV("valor_adicional_descargue", "0")}</VALORADICIONALDESCARGUE>");
        xml.AppendLine($"    <VALORADICIONAL>{v.GetV("valor_adicional", "0")}</VALORADICIONAL>");
        xml.AppendLine($"    <MOTIVOVALORADICIONAL>{v.GetV("motivo_valor_adicional", "")}</MOTIVOVALORADICIONAL>");
        xml.AppendLine($"    <VALORDESCUENTO>{v.GetV("valor_descuento", "0")}</VALORDESCUENTO>");
        xml.AppendLine($"    <MOTIVOVALORDESCUENTO>{v.GetV("motivo_descuento", "")}</MOTIVOVALORDESCUENTO>");
        xml.AppendLine($"    <VALORSOBREANTICIPO>{v.GetV("valor_sobreanticipo", "0")}</VALORSOBREANTICIPO>");
        xml.AppendLine($"    <OBSERVACIONES>{Escape(v.GetV("observaciones", "NINGUNA"))}</OBSERVACIONES>");
        xml.AppendLine("  </variables>");
        xml.AppendLine("</root>");
        return xml.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

public static class DictionaryExtensions
{
    public static string GetV(this Dictionary<string, string> dict, string key, string defaultValue = "")
        => dict.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val) ? val : defaultValue;
}
