using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json;

namespace TransportesGutierrez.Api.Services;

public sealed class AppSessionService
{
    private readonly ITimeLimitedDataProtector _protector;

    public AppSessionService(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("CargoDespacho.AppSession.v1")
            .ToTimeLimitedDataProtector();

    public string Create(string userId, string role) => _protector.Protect(
        JsonConvert.SerializeObject(new AppSession(userId, role)),
        DateTimeOffset.UtcNow.AddHours(8));

    public AppSession? Read(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return JsonConvert.DeserializeObject<AppSession>(
                _protector.Unprotect(authorization[7..], out _));
        }
        catch { return null; }
    }
}

public sealed record AppSession(string UserId, string Role);
