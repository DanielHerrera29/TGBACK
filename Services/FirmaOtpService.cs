using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using Newtonsoft.Json.Linq;
namespace TransportesGutierrez.Api.Services;

public sealed class FirmaOtpService(HttpClient http, IConfiguration config, SupabaseService db, ILogger<FirmaOtpService> logger)
{
    // Separate account. Never fall back to Brevo:ApiKey (PDF delivery).
    private string ApiKey => config["Brevo_Codigo_verificacionAPIKEY"] ?? "";
    private string Sender => config["Brevo_Codigo_verificacionSenderEmail"] ?? "";
    public bool Required => config.GetValue("FirmaOtp:Required", true);
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey) && ValidEmail(Sender);
    public static bool ValidEmail(string? value) => value is {Length: >3 and <=254} && !value.Any(char.IsWhiteSpace) && MailAddress.TryCreate(value,out var a) && a.Address==value && value.Contains('.');
    public string CodeHash(string user,string order,string id,string code) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiKey),Encoding.UTF8.GetBytes($"TEG-firma-email-v1:{user}:{order}:{id}:{code}")));
    public Task<JObject> Operation(string user,string order,string action,string? id=null,string? name=null,string? email=null,string? hash=null,string? message=null) => db.FirmaOtpAsync(new {p_usuario=user,p_orden=order,p_accion=action,p_id=id,p_nombre=name,p_email=email,p_hash=hash,p_message=message});
    public async Task<bool> Authorized(string user,string order) => !Required || (bool?)(await Operation(user,order,"AUTORIZAR"))["autorizada"]==true;
    public async Task<JObject> Start(string user,string order,string name,string email)
    {
        if(!Configured) throw new InvalidOperationException("Cuenta de códigos no configurada.");
        var id=Guid.NewGuid().ToString();
        var code=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");
        var row=await Operation(user,order,"SOLICITAR",id:id,name:name,email:email.Trim().ToLowerInvariant(),hash:CodeHash(user,order,id,code));
        if((bool?)row["enviar"]!=true)
        {
            // La base de datos decidio no reenviar (ya hay un codigo vigente para esta orden).
            // No se llama a Brevo en este caso: por eso no llega ningun correo nuevo.
            logger.LogInformation("OTP firma: orden {Orden} no requiere reenvio. estado={Estado} vence={Vence}", order, (string?)row["estado"], (string?)row["vence_at"]);
            return row;
        }
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key",ApiKey);
        request.Content=JsonContent.Create(new {
            sender=new {email=Sender,name=config["Brevo_Codigo_verificacionSenderName"] ?? "TRANSPORTE GUTIERREZ"},
            to=new[]{new{email=email.Trim().ToLowerInvariant(),name}},
            subject="Código para habilitar la firma — TEG",
            textContent=$"Su código para habilitar la firma de la orden {order} es: {code}\nVence en 10 minutos. Ingréselo en la app únicamente si está participando en esta orden. Este código no firma el documento: habilita el botón Firmar. Si no lo solicitó, ignore este correo.",
            tags=new[]{"codigo-firma"}
        });
        using var response=await http.SendAsync(request);
        // No se registra el destinatario ni el codigo. Si registramos el estado y el cuerpo de error de Brevo
        // (nunca contiene el codigo ni el destinatario) para poder diagnosticar fallos de envio.
        if(!response.IsSuccessStatusCode)
        {
            var errorBody=await response.Content.ReadAsStringAsync();
            logger.LogWarning("OTP firma: Brevo respondio {StatusCode} al enviar el codigo de la orden {Orden}. Cuerpo: {Body}", (int)response.StatusCode, order, errorBody);
            throw new HttpRequestException("Brevo no confirmó el envío del código.");
        }
        var payload=JObject.Parse(await response.Content.ReadAsStringAsync());
        var message=(string?)payload["messageId"];
        if(string.IsNullOrWhiteSpace(message))
        {
            logger.LogWarning("OTP firma: Brevo respondio 200 sin messageId para la orden {Orden}.", order);
            throw new HttpRequestException("Brevo no devolvió referencia de envío.");
        }
        return await Operation(user,order,"ENVIADO",id:id,message:message);
    }
    public Task<JObject> Check(string user,string order,string id,string code) => Operation(user,order,"COMPROBAR",id:id,hash:CodeHash(user,order,id,code));
}
