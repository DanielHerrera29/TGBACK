using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TransportesGutierrez.Api.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ErrorController : ControllerBase
{
    [Route("/error")]
    public IActionResult Error()
    {
        var exception = HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = HttpContext.TraceIdentifier;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ocurrió un error procesando la solicitud.",
            Detail = HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? exception?.Message
                : null
        };
        problem.Extensions["traceId"] = traceId;

        return StatusCode(problem.Status.Value, problem);
    }
}
