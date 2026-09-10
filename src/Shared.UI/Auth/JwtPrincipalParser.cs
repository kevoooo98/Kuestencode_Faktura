using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Kuestencode.Shared.UI.Auth;

/// <summary>
/// Extracts and parses the werkbank JWT from an HTTP request. Shared between
/// <see cref="PassThroughAuthStateProvider"/> and <see cref="JwtUserContextMiddleware"/>
/// so both populate identity from exactly the same token/claim logic.
/// </summary>
public static class JwtPrincipalParser
{
    public static string? ExtractToken(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue("werkbank_auth_cookie", out var cookie)
            && !string.IsNullOrEmpty(cookie))
            return cookie;

        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim();

        return null;
    }

    public static ClaimsPrincipal? Parse(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo < DateTime.UtcNow) return null;

            var claims = jwt.Claims.Select(c => new Claim(
                c.Type switch
                {
                    "role"   => ClaimTypes.Role,
                    "name"   => ClaimTypes.Name,
                    "nameid" => ClaimTypes.NameIdentifier,
                    "email"  => ClaimTypes.Email,
                    _        => c.Type
                }, c.Value));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        }
        catch { return null; }
    }
}
