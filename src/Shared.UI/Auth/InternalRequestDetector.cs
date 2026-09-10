using System.Net;
using System.Security.Claims;
using Kuestencode.Shared.Contracts.Host;
using Microsoft.AspNetCore.Http;

namespace Kuestencode.Shared.UI.Auth;

/// <summary>
/// Erkennt Server-zu-Server-Aufrufe zwischen Werkbank-Containern im Docker-Netzwerk
/// (z. B. Saldo -> Faktura fuer EUER-Daten), die ohne Nutzer-JWT erfolgen, weil die
/// aufrufenden HttpClients keinen Browser-Token weiterreichen. Ohne diese Erkennung
/// wuerde eine strikte Rollenpruefung ([RequireRole]) auch legitime interne Aufrufe blocken.
///
/// Der Host-Container wird bewusst NICHT als "intern" behandelt, obwohl er im selben
/// Docker-Netz liegt: Host proxied auch (moeglicherweise nicht authentifizierten)
/// Browser-Traffic an die Module weiter, und dieser sieht aus Sicht des empfangenden
/// Moduls netzwerktechnisch identisch zu einem echten Modul-zu-Modul-Aufruf aus. Wuerde
/// Host pauschal vertraut, koennte ein anonymer Browser ueber Host jedes Modul ohne Login
/// erreichen. Nur andere Modul-Container (die selbst nie fremden Traffic weiterleiten)
/// gelten daher als vertrauenswuerdig intern.
/// </summary>
public static class InternalRequestDetector
{
    private static readonly Lazy<HashSet<string>> HostContainerIps = new(ResolveHostIps);

    private static HashSet<string> ResolveHostIps()
    {
        try
        {
            // Läuft ausschließlich in ASP.NET-Core-Middleware auf dem Server, nie im Browser/WASM.
#pragma warning disable CA1416
            return Dns.GetHostAddresses("host").Select(a => a.ToString()).ToHashSet();
#pragma warning restore CA1416
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    public static bool IsInternal(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null) return false;

        // Normalize IPv6-mapped IPv4 to plain IPv4
        var ip = remoteIp.IsIPv4MappedToIPv6
            ? remoteIp.MapToIPv4().ToString()
            : remoteIp.ToString();

        if (HostContainerIps.Value.Contains(ip))
            return false;

        if (ip.StartsWith("172.") || ip.StartsWith("10.") || ip.StartsWith("192.168."))
            return true;

        // IPv6 link-local (fe80::) und unique-local (fd/fc)
        if (ip.StartsWith("fe80:") || ip.StartsWith("fd") || ip.StartsWith("fc"))
            return true;

        return false;
    }

    public static ClaimsPrincipal CreateInternalServicePrincipal()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
            new Claim(ClaimTypes.Name, "InternalModule"),
            new Claim(ClaimTypes.Role, UserRole.Admin.ToString())
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "InternalService"));
    }
}
