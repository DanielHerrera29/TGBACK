using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TransportesGutierrez.Api.Controllers;
using TransportesGutierrez.Api.Services;
using TransportesGutierrez.Api.Configurations;
using TransportesGutierrez.Api.Dtos;

var sessions = new AppSessionService(new EphemeralDataProtectionProvider());
var fake = new FakeHttp();
var controller = new ServiciosController(sessions,fake,Options.Create(new SupabaseOptions {Url="https://example.invalid",Key="fixture"}));
controller.ControllerContext = new ControllerContext { HttpContext=new DefaultHttpContext() };
var request = new GuardarBorradorRequest {Clave=Guid.NewGuid(),Datos=JsonSerializer.SerializeToElement(new {p_usuario="spoofed"})};
Check(await controller.Guardar(request,default) is UnauthorizedResult,"sin sesión");
var user=Guid.NewGuid().ToString();
controller.Request.Headers.Authorization="Bearer "+sessions.Create(user,"operator");
Check(await controller.Guardar(new GuardarBorradorRequest(),default) is BadRequestObjectResult,"clave obligatoria");
fake.Body="{\"id\":\"fixture\"}";
Check(await controller.Guardar(request,default) is ContentResult,"contrato JSON");
using(var parsed=JsonDocument.Parse(fake.Sent!)) {Check(parsed.RootElement.GetProperty("p_usuario").GetString()==user,"actor del token");}
fake.Status=HttpStatusCode.BadRequest; fake.Body="{\"code\":\"P0001\"}";
Check((await controller.Guardar(request,default) as ObjectResult)?.StatusCode==409,"conflicto recuperable");
fake.Body="{\"code\":\"42501\"}";
Check((await controller.Guardar(request,default) as ObjectResult)?.StatusCode==403,"permiso denegado");
fake.Body="upstream unavailable";
Check((await controller.Guardar(request,default) as ObjectResult)?.StatusCode==503,"respuesta no JSON");
var old=new OrdenesEscoltaController(sessions,null!,null!,NullLogger<OrdenesEscoltaController>.Instance);
old.ControllerContext=controller.ControllerContext;
Check((old.Reservar(new CrearOrdenEscoltaDto()) as ObjectResult)?.StatusCode==426,"ruta antigua no crea orden");
var clientRequest = new CrearClienteRequest { Id=Guid.NewGuid(), Tipo="empresa", Nombre="Cliente prueba", Documento="900123456" };
controller.Request.Headers.Authorization="";
Check(await controller.CrearCliente(clientRequest,default) is UnauthorizedResult,"cliente requiere sesión");
controller.Request.Headers.Authorization="Bearer "+sessions.Create(user,"operator");
fake.Replies.Enqueue((HttpStatusCode.OK,"[{\"role\":\"escolta\"}]"));
Check((await controller.CrearCliente(clientRequest,default) as StatusCodeResult)?.StatusCode==403,"escolta no administra clientes");
fake.Replies.Enqueue((HttpStatusCode.OK,"[]"));
Check((await controller.CrearCliente(clientRequest,default) as StatusCodeResult)?.StatusCode==403,"usuario inactivo denegado");
fake.Replies.Enqueue((HttpStatusCode.OK,"[{\"role\":\"admin\"}]"));
fake.Replies.Enqueue((HttpStatusCode.OK,"[]"));
fake.Replies.Enqueue((HttpStatusCode.Created,"[{\"id\":\""+clientRequest.Id+"\"}]"));
Check(await controller.CrearCliente(clientRequest,default) is ContentResult,"crear cliente con usuario activo");
using(var payload=JsonDocument.Parse(fake.Sent!)) Check(payload.RootElement.GetProperty("id").GetGuid()==clientRequest.Id,"identidad estable del cliente");
var inserts = fake.Posts;
fake.Replies.Enqueue((HttpStatusCode.OK,"[{\"role\":\"admin\"}]"));
fake.Replies.Enqueue((HttpStatusCode.OK,"[{\"nombre\":\"Cliente prueba\",\"nit_o_documento\":\"900123456\",\"tipo_cliente\":\"empresa\"}]"));
Check(await controller.CrearCliente(clientRequest,default) is ContentResult && fake.Posts==inserts,"reintento no inserta otro cliente");
fake.Replies.Enqueue((HttpStatusCode.OK,"[{\"role\":\"admin\"}]"));
fake.Replies.Enqueue((HttpStatusCode.OK,"[]"));
fake.Replies.Enqueue((HttpStatusCode.Conflict,"{}"));
Check(await controller.CrearCliente(clientRequest,default) is ConflictResult,"documento duplicado no se reemplaza");
Check(!ModuleAccessFilter.Permite("operator", "GET", "/api/remesa"), "operador no consulta remesas");
Check(!ModuleAccessFilter.Permite("operator", "POST", "/api/manifiesto/generar"), "operador no genera manifiestos");
Check(!ModuleAccessFilter.Permite("operator", "POST", "/api/servicios/clientes"), "operador no crea clientes");
Check(ModuleAccessFilter.Permite("operator", "GET", "/api/servicios/clientes"), "operador consulta catálogo");
Check(ModuleAccessFilter.Permite("operator", "POST", "/api/ordenes-escolta/abc/enviar"), "operador envía su orden con validación del controlador");
var noMailConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
var mail = new EmailSender(noMailConfig, fake.CreateClient("mail"));
var db = new SupabaseService(fake, Options.Create(new SupabaseOptions {Url="https://example.invalid",Key="fixture"}), NullLogger<SupabaseService>.Instance);
var orders = new OrdenesEscoltaController(sessions,db,mail,NullLogger<OrdenesEscoltaController>.Instance);
orders.ControllerContext=controller.ControllerContext;
fake.Replies.Enqueue((HttpStatusCode.OK,"[{\"id\":\"fixture\",\"consecutivo\":36,\"created_by\":\""+user+"\",\"pdf_path\":null}]"));
var before = fake.Posts;
Check((await orders.Enviar("fixture", new EnviarOrdenEscoltaDto { PdfBase64=Convert.ToBase64String(new byte[]{1,2,3}) }, default) as ObjectResult)?.StatusCode==503,"configuración ausente explica error antes de reclamar");
Check(fake.Posts==before,"sin configuración no se reclama entrega ni se envía correo");
async Task<Microsoft.AspNetCore.Mvc.IActionResult?> FilterResult(string tokenRole, string? currentRole, string path) {
    var ctx = new DefaultHttpContext();
    ctx.Request.Path = path;
    ctx.Request.Method = "GET";
    ctx.Request.Headers.Authorization="Bearer "+sessions.Create(user,tokenRole);
    fake.Replies.Enqueue((HttpStatusCode.OK,currentRole is null ? "[]" : "[{\"role\":\""+currentRole+"\"}]"));
    var action = new ActionContext(ctx, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
    var authorization = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(action, new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());
    await new ModuleAccessFilter(sessions,db).OnAuthorizationAsync(authorization);
    return authorization.Result;
}
Check((await FilterResult("operator","operator","/api/remesa") as ObjectResult)?.StatusCode==403,"filtro bloquea URL de remesas al operador");
Check(await FilterResult("admin","operator","/api/ordenes-escolta") is UnauthorizedResult,"cambio de rol invalida sesión administrativa");
Check(await FilterResult("operator",null,"/api/ordenes-escolta") is UnauthorizedResult,"usuario desactivado no puede continuar");
Console.WriteLine("25 comprobaciones de contrato aprobadas; HTTP simulado, sin base ni correo.");
static void Check(bool value,string name) {if(!value)throw new Exception("Falló: "+name);}

sealed class FakeHttp : HttpMessageHandler,IHttpClientFactory {
 public HttpStatusCode Status=HttpStatusCode.OK;
 public string Body="{}";
 public string? Sent;
 public int Posts;
 public Queue<(HttpStatusCode Status,string Body)> Replies = new();
 public HttpClient CreateClient(string name)=>new(this,false);
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
   Sent=request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
   if(request.Method==HttpMethod.Post) Posts++;
   var reply=Replies.Count>0 ? Replies.Dequeue() : (Status,Body);
   return new HttpResponseMessage(reply.Item1){Content=new StringContent(reply.Item2)};
 }
}
