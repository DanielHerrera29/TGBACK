using System.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security;
using System.Xml.Linq;
using TransportesGutierrez.Api.Models;

namespace TransportesGutierrez.Api.Services;

public class RndcClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RndcClient> _logger;
    private readonly IConfiguration _config;

    private const string SoapNamespace = "urn:BPMServicesIntf-IBPMServices";
    private const string SoapEncodingStyle = "http://schemas.xmlsoap.org/soap/encoding/";
    public RndcClient(HttpClient http, ILogger<RndcClient> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _config = config;
    }

    public async Task<RndcResponse> EnviarAsync(string xmlPayload, int procesoId, int tipo = 1, string simulacion = "N")
    {
        if (_config.GetValue<bool>("RndcStub"))
        {
            await Task.Delay(800);
            var fakeRadicado = procesoId switch
            {
                3 => $"REM-STUB-{DateTime.Now:MMddHHmm}",
                4 => $"AUT-STUB-{DateTime.Now:MMddHHmm}",
                5 => $"CUM-REM-STUB-{DateTime.Now:MMddHHmm}",
                6 => $"CUM-MAN-STUB-{DateTime.Now:MMddHHmm}",
                12 => $"VEH-STUB-{DateTime.Now:MMddHHmm}",
                73 => $"FIRMA-STUB-{DateTime.Now:MMddHHmm}",
                _ => $"STUB-{procesoId}-{DateTime.Now:MMddHHmm}"
            };

            _logger.LogWarning("MODO STUB — radicado ficticio: {Radicado}", fakeRadicado);

            return new RndcResponse
            {
                Exito = true,
                Radicado = fakeRadicado,
                NumeroAutorizacion = fakeRadicado,
                XmlEnviado = xmlPayload,
                XmlRespuesta = $"<stub><radicado>{fakeRadicado}</radicado></stub>"
            };
        }

        try
        {
            var method = GetSoapMethod();
            var soapEnvelope = BuildSoapEnvelope(xmlPayload, method);
            var encoding = GetRndcEncoding();
            var url = ResolveHost(procesoId, tipo, simulacion);

            _logger.LogInformation(
                "Enviando Proceso {ProcesoId} tipo {Tipo} simulacion {Simulacion} a RNDC host {Host} metodo {Method}",
                procesoId, tipo, simulacion, url, method);
            _logger.LogInformation(
                "Diagnostico SOAP RNDC: action={SoapAction} envelope=rpc-encoded requestLength={RequestLength} requestSha256={RequestHash}",
                $"{SoapNamespace}#{method}",
                encoding.GetByteCount(xmlPayload),
                Sha256(xmlPayload));

            var response = await SendSoapRequestAsync(url, soapEnvelope, method, encoding);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Respuesta RNDC Proceso {ProcesoId}: {Status}", procesoId, response.StatusCode);
            _logger.LogInformation("Respuesta RNDC raw: {Body}", responseBody);

            if (response.IsSuccessStatusCode)
                return ParseRndcResponse(responseBody, xmlPayload);

            var parsed = ParseRndcResponse(responseBody, xmlPayload);
            if (!parsed.Exito && string.IsNullOrWhiteSpace(parsed.Error))
                parsed.Error = $"RNDC respondio HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            return parsed;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error conectando al WS RNDC Proceso {ProcesoId}", procesoId);
            return new RndcResponse
            {
                Exito = false,
                Error = $"No se pudo conectar al Web Service RNDC: {ex.Message}",
                XmlEnviado = xmlPayload
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado RNDC Proceso {ProcesoId}", procesoId);
            return new RndcResponse
            {
                Exito = false,
                Error = ex.Message,
                XmlEnviado = xmlPayload
            };
        }
    }

    private string ResolveHost(int procesoId, int tipo, string simulacion)
    {
        // RNDC usa el mismo Web Service para validar y registrar.
        // El modo de prueba se define en el XML con <simulacion>S</simulacion>.
        var hosts = _config.GetSection("Rndc:Hosts");

        string key = procesoId switch
        {
            // Expedir Remesa (P3) y Manifiesto (P4) — tipo 1 (escritura)
            3 when tipo == 1 => "Expedicion",
            4 when tipo == 1 => "Expedicion",

            // Cualquier consulta (tipo 3 o 6) — incluye diccionarios, consultas de firma, etc.
            _ when tipo == 3 || tipo == 6 => "Consulta",

            // El resto de procesos cuando NO son consulta
            _ => "General"
        };

        var url = hosts[key];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Host '{Key}' no configurado en Rndc:Hosts, usando General", key);
            url = hosts["General"]
                ?? "http://rndcws.mintransporte.gov.co:8080/soap/IBPMServices";
        }

        return url;
    }

    private async Task<HttpResponseMessage> SendSoapRequestAsync(string url, string soapEnvelope, string method, Encoding encoding)
    {
        var bytes = encoding.GetBytes(soapEnvelope);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        // El binding SOAP 1.1 del RNDC es rpc/encoded. En SOAP 1.1 el
        // SOAPAction debe viajar como cadena entre comillas, no como URI plano.
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{SoapNamespace}#{method}\"");

        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml")
        {
            CharSet = encoding.WebName
        };

        return await _http.SendAsync(request);
    }

    private string GetSoapMethod()
        => _config.GetValue<string>("Rndc:Method")?.Trim() switch
        {
            "AtenderMensajeBPM" => "AtenderMensajeBPM",
            "AtenderMensajeRNDC" => "AtenderMensajeRNDC",
            _ => "AtenderMensajeRNDC"
        };

    private Encoding GetRndcEncoding()
    {
        var encodingName = _config.GetValue<string>("Rndc:Encoding") ?? "ISO-8859-1";
        return Encoding.GetEncoding(encodingName);
    }

    private static string BuildSoapEnvelope(string xmlPayload, string method)
    {
        // Request es xs:string en el WSDL. El RNDC devuelve su respuesta XML
        // como texto escapado; enviarlo igual evita que el servidor legacy trate
        // el CDATA como un nodo en vez de como el valor del parametro Request.
        var requestValue = SecurityElement.Escape(xmlPayload) ?? string.Empty;

        return $@"<?xml version=""1.0"" encoding=""ISO-8859-1""?>
<SOAP-ENV:Envelope
    xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/""
    xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
    xmlns:SOAP-ENC=""http://schemas.xmlsoap.org/soap/encoding/"">
  <SOAP-ENV:Body SOAP-ENV:encodingStyle=""{SoapEncodingStyle}"">
    <NS1:{method} xmlns:NS1=""{SoapNamespace}"">
      <Request xsi:type=""xsd:string"">{requestValue}</Request>
    </NS1:{method}>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    private static string Sha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }


    private static RndcResponse ParseRndcResponse(string responseBody, string xmlEnviado)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return new RndcResponse
                {
                    Exito = false,
                    Error = "Respuesta RNDC vacia",
                    XmlEnviado = xmlEnviado,
                    XmlRespuesta = responseBody
                };
            }

            var doc = XDocument.Parse(responseBody);
            var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Fault", StringComparison.OrdinalIgnoreCase));
            if (fault != null)
            {
                var faultString = fault.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("faultstring", StringComparison.OrdinalIgnoreCase))?.Value
                    ?? fault.Value;
                return new RndcResponse
                {
                    Exito = false,
                    Error = $"SOAP Fault RNDC: {faultString}",
                    XmlEnviado = xmlEnviado,
                    XmlRespuesta = responseBody
                };
            }

            var returnEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("return", StringComparison.OrdinalIgnoreCase)
                                  || e.Name.LocalName.Equals("AtenderMensajeRNDCReturn", StringComparison.OrdinalIgnoreCase)
                                  || e.Name.LocalName.Equals("AtenderMensajeBPMReturn", StringComparison.OrdinalIgnoreCase));

            if (returnEl == null)
            {
                return new RndcResponse
                {
                    Exito = false,
                    Error = "Respuesta RNDC sin elemento <return>",
                    XmlEnviado = xmlEnviado,
                    XmlRespuesta = responseBody
                };
            }

            var innerXml = returnEl.Value?.Trim();
            if (string.IsNullOrWhiteSpace(innerXml))
            {
                return new RndcResponse
                {
                    Exito = false,
                    Error = "Respuesta RNDC con <return> vacio",
                    XmlEnviado = xmlEnviado,
                    XmlRespuesta = responseBody
                };
            }

            var innerDoc = XDocument.Parse(innerXml);

            var radicado = GetFirstValue(innerDoc, "ingresoid", "radicado", "RADICADO", "numeroAutorizacion", "NumeroAutorizacion");

            var error = GetFirstValue(innerDoc, "error", "ERROR", "ErrorMSG", "ErrorMsg", "MensajeError", "mensajeerror", "descripcionerror");

            var codigoError = GetFirstValue(innerDoc, "codigoerror", "CODIGOERROR", "CodigoError", "ErrorCode", "codigo");

            bool exito = !string.IsNullOrEmpty(radicado) && string.IsNullOrEmpty(error);

            return new RndcResponse
            {
                Exito = exito,
                Radicado = radicado,
                NumeroAutorizacion = radicado,
                Error = error,
                CodigoError = codigoError,
                XmlEnviado = xmlEnviado,
                XmlRespuesta = responseBody
            };
        }
        catch (Exception ex)
        {
            return new RndcResponse
            {
                Exito = false,
                Error = $"Error parseando respuesta RNDC: {ex.Message}",
                XmlEnviado = xmlEnviado,
                XmlRespuesta = responseBody
            };
        }
    }

    private static string? GetFirstValue(XDocument doc, params string[] names)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return doc.Descendants()
            .FirstOrDefault(e => wanted.Contains(e.Name.LocalName) && !string.IsNullOrWhiteSpace(e.Value))
            ?.Value
            ?.Trim();
    }
}
