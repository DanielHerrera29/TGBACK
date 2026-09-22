using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TransportesGutierrez.Api.Services;

public sealed class ModuleAccessFilter(AppSessionService sessions, SupabaseService db) : IAsyncAuthorizationFilter
{
    public static bool Permite(string role, string method, string path)
    {
        if (role == "admin") return true;
        if (role == "administrativo") return !path.StartsWith("/api/usuarios", StringComparison.OrdinalIgnoreCase);
        if (role == "auditor") return method == "GET" && !path.StartsWith("/api/usuarios", StringComparison.OrdinalIgnoreCase);
        if (role is not ("operator" or "escolta")) return false;
        if (path.StartsWith("/api/ordenes-escolta", StringComparison.OrdinalIgnoreCase)) return true;
        if (method == "GET" && path == "/api/servicios/clientes") return true;
        if (method == "POST" && path == "/api/servicios/orden-borrador") return true;
        return method == "GET" && path.StartsWith("/api/servicios/orden-borrador/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        var path = request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path == "/api/sesion/iniciar") return;
        if (request.Method == "GET" && path is "/api/rndc/ping" or "/api/rndc/health") return;
        var session = sessions.Read(request.Headers.Authorization);
        if (session is null) { context.Result = new UnauthorizedResult(); return; }
        var currentRole = await db.RolActivoAsync(session.UserId);
        // Role changes invalidate existing sessions, including formerly administrative tokens.
        if (currentRole is null || currentRole != session.Role) { context.Result = new UnauthorizedResult(); return; }
        if (!Permite(currentRole, request.Method, path))
            context.Result = new ObjectResult(new { error = "Su rol no tiene acceso a este módulo." }) { StatusCode = 403 };
    }
}