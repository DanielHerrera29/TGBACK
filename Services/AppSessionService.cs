using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace TransportesGutierrez.Api.Services;

// Antes esto usaba IDataProtectionProvider: sus llaves se generan de nuevo en cada contenedor
// (Render lo advierte en cada deploy: "Protected data will be unavailable when container is
// destroyed"), asi que CADA despliegue invalidaba todas las sesiones activas -> 401 en toda la
// app para cualquiera que ya hubiera iniciado sesion. Ahora se firma con una clave fija
// (Sessions:Secret, variable de entorno Sessions__Secret) que no cambia entre despliegues.
public sealed class AppSessionService
{
    private readonly byte[] _key;

    public AppSessionService(IConfiguration config)
    {
        var secret = config["Sessions:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Configure Sessions:Secret mediante la variable de entorno Sessions__Secret.");
        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string Create(string userId, string role)
    {
        var payload = JsonConvert.SerializeObject(new AppSessionPayload(userId, role, DateTimeOffset.UtcNow.AddHours(8)));
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sig = HMACSHA256.HashData(_key, payloadBytes);
        return Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(sig);
    }

    public AppSession? Read(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var parts = authorization[7..].Split('.');
            if (parts.Length != 2) return null;
            var payloadBytes = Convert.FromBase64String(parts[0]);
            var sig = Convert.FromBase64String(parts[1]);
            var expected = HMACSHA256.HashData(_key, payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(sig, expected)) return null;
            var payload = JsonConvert.DeserializeObject<AppSessionPayload>(Encoding.UTF8.GetString(payloadBytes));
            if (payload is null || payload.Exp < DateTimeOffset.UtcNow) return null;
            return new AppSession(payload.UserId, payload.Role);
        }
        catch { return null; }
    }
}

public sealed record AppSessionPayload(string UserId, string Role, DateTimeOffset Exp);
public sealed record AppSession(string UserId, string Role);
