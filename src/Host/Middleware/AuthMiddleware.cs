using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kuestencode.Werkbank.Host.Data;
using Kuestencode.Werkbank.Host.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kuestencode.Werkbank.Host.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthMiddleware> _logger;

    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/api/modules/register",
        "/api/modules/health",
        "/api/setup/required",
        "/api/setup/complete"
    };

    private static readonly string[] PublicPathPrefixes = new[]
    {
        "/api/mobile/",  // Mobile API (Token-Status, PIN setzen/prüfen)
        "/m/"            // Mobile Blazor-Seiten
    };

    public AuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Statische Dateien, Blazor-Hub und öffentliche API-Pfade durchlassen
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // Auth-Status aus DB prüfen
        using var scope = context.RequestServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HostDbContext>();
        var settings = await dbContext.WerkbankSettings.AsNoTracking().FirstOrDefaultAsync();

        if (settings == null || !settings.AuthEnabled)
        {
            // Auth deaktiviert: impliziter Admin-User
            var adminClaims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
                new Claim(ClaimTypes.Name, "Admin"),
                new Claim(ClaimTypes.Role, UserRole.Admin.ToString())
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(adminClaims, "NoAuth"));

            await _next(context);
            return;
        }

        var token = ExtractToken(context);

        // WICHTIG: Host ist die einzige nach außen (und im LAN) erreichbare Komponente —
        // anders als bei den Modulen (JwtUserContextMiddleware) darf hier NICHT anhand der
        // Quell-IP auf "intern" geschlossen werden: Docker leitet auch echten Browser-Traffic
        // von externen/LAN-Clients über den veröffentlichten Port so weiter, dass er wie ein
        // Docker-internes Netz aussieht (private IP-Bereiche). Ein solcher IP-Check hätte
        // externen Zugriff ohne Login erlaubt. Echte interne Technik-Aufrufe (Registrierung,
        // Healthcheck) sind stattdessen explizit in PublicPaths/PublicPathPrefixes gelistet.

        // Auth aktiviert: JWT prüfen
        if (string.IsNullOrEmpty(token))
        {
            // API-Requests bekommen 401
            if (IsApiPath(path))
            {
                var remoteIp = context.Connection.RemoteIpAddress;
                var hostHeader = context.Request.Host.Host;
                _logger.LogWarning("AuthMiddleware: 401 no token. Path={Path}, RemoteIP={RemoteIP}, Host={Host}",
                    path, remoteIp, hostHeader);
                context.Response.StatusCode = 401;
                return;
            }

            // Blazor/Page-Requests durchlassen (AuthStateProvider handled redirect)
            await _next(context);
            return;
        }

        var principal = ValidateToken(token);
        if (principal == null)
        {
            // Ungültiger Token: API-Requests bekommen 401
            if (IsApiPath(path))
            {
                _logger.LogWarning("AuthMiddleware: 401 invalid token. Path={Path}, Host={Host}, HasAuthHeader={HasAuthHeader}",
                    path,
                    context.Request.Host.Host,
                    context.Request.Headers.ContainsKey("Authorization"));
                context.Response.StatusCode = 401;
                return;
            }

            await _next(context);
            return;
        }

        // Gültiger Token: User-Context setzen
        context.User = principal;
        await _next(context);
    }

    /// <summary>
    /// Erkennt API-Aufrufe sowohl am Host selbst ("/api/...") als auch an über YARP
    /// weitergeleitete Modul-Endpunkte ("/{modul}/api/...", z. B. "/recepta/api/recepta/documents").
    /// Ohne diesen Check würden proxied Modul-API-Aufrufe fälschlich als "Page-Request"
    /// durchgelassen, wenn kein Token vorhanden ist (siehe fehlende [RequireRole]-Absicherung).
    /// </summary>
    private static bool IsApiPath(string path) =>
        path.Contains("/api/", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;

        // Statische Dateien
        if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/company/logos", StringComparison.OrdinalIgnoreCase) ||
            path.Contains('.'))
        {
            return true;
        }

        // Public Path Prefixes (z.B. /api/mobile/, /m/)
        if (PublicPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Öffentliche Auth-Endpoints
        if (PublicPaths.Contains(path))
            return true;

        // Invite/Reset-Token-Validierung und -Annahme
        if (path.StartsWith("/api/team-members/invite/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/reset/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/invite/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/reset/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/login/mfa", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/login/mfa", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/forgot-password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? ExtractToken(HttpContext context)
    {
        // First try Authorization header
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        // Fallback to cookie
        if (context.Request.Cookies.TryGetValue("werkbank_auth_cookie", out var cookieToken))
        {
            return cookieToken;
        }

        return null;
    }

    private ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var secret = GetJwtSecret();
            if (string.IsNullOrEmpty(secret))
                return null;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var issuer = _configuration["Jwt:Issuer"] ?? "KuestencodeWerkbank";

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = issuer,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, parameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "JWT-Validierung fehlgeschlagen");
            return null;
        }
    }

    private string? GetJwtSecret()
    {
        var secret = _configuration["Jwt:Secret"];
        if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 32)
            return secret;

        var filePath = Path.Combine(AppContext.BaseDirectory, "data", "jwt-secret.txt");
        if (File.Exists(filePath))
        {
            var stored = File.ReadAllText(filePath).Trim();
            if (stored.Length >= 32)
                return stored;
        }

        return null;
    }
}
