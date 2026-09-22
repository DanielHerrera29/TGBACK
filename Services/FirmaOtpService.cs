using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using Newtonsoft.Json.Linq;
namespace TransportesGutierrez.Api.Services;

public sealed class FirmaOtpService(HttpClient http, IConfiguration config, SupabaseService db)
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
        if((bool?)row["enviar"]!=true) return row;
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
        // Do not log provider body, code, API key or recipient.
        if(!response.IsSuccessStatusCode) throw new HttpRequestException("Brevo no confirmó el envío del código.");
        var payload=JObject.Parse(await response.Content.ReadAsStringAsync());
        var message=(string?)payload["messageId"];
        if(string.IsNullOrWhiteSpace(message)) throw new HttpRequestException("Brevo no devolvió referencia de envío.");
        return await Operation(user,order,"ENVIADO",id:id,message:message);
    }
    public Task<JObject> Check(string user,string order,string id,string code) => Operation(user,order,"COMPROBAR",id:id,hash:CodeHash(user,order,id,code));
}
