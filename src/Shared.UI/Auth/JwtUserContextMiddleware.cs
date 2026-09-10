using Microsoft.AspNetCore.Http;

namespace Kuestencode.Shared.UI.Auth;

/// <summary>
/// Populates HttpContext.User from the werkbank JWT for API/service requests within a module
/// process (Blazor circuits resolve identity separately via <see cref="PassThroughAuthStateProvider"/>).
/// Without this, HttpContext.User stays the anonymous default even when a valid token is present,
/// so controllers/services can never determine "who" made a request (e.g. for audit logging).
/// Requests without a token from another Werkbank container (e.g. Saldo calling Faktura's API
/// directly for EÜR-Daten, ohne Browser-Token) erhalten einen internen Service-Principal, damit
/// [RequireRole] diese legitimen Server-zu-Server-Aufrufe nicht blockiert. Echte Browser-Zugriffe
/// von außen benötigen weiterhin immer einen gültigen Token.
/// </summary>
public class JwtUserContextMiddleware
{
    private readonly RequestDelegate _next;

    public JwtUserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var token = JwtPrincipalParser.ExtractToken(context);
        if (!string.IsNullOrEmpty(token))
        {
            var principal = JwtPrincipalParser.Parse(token);
            if (principal != null)
            {
                context.User = principal;
            }
        }
        else if (InternalRequestDetector.IsInternal(context))
        {
            context.User = InternalRequestDetector.CreateInternalServicePrincipal();
        }

        await _next(context);
    }
}
