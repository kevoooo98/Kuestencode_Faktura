using System.Security.Claims;
using Kuestencode.Shared.Contracts.Host;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kuestencode.Shared.UI.Auth;

/// <summary>
/// Schränkt Zugriff auf API-Endpunkte auf bestimmte Rollen ein.
/// Kann auf Controller-Klassen und einzelne Actions angewendet werden.
/// Bei deaktivierter Auth (Identity.AuthenticationType == "NoAuth") wird der Zugriff immer gewährt.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    public UserRole[] AllowedRoles { get; }

    public RequireRoleAttribute(params UserRole[] roles)
    {
        AllowedRoles = roles;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // Auth deaktiviert → alles erlaubt
        if (user.Identity?.AuthenticationType == "NoAuth")
            return;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var roleClaim = user.FindFirstValue(ClaimTypes.Role)
            ?? user.FindFirstValue("role")
            ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrEmpty(roleClaim) ||
            !Enum.TryParse<UserRole>(roleClaim, ignoreCase: true, out var userRole) ||
            !AllowedRoles.Contains(userRole))
        {
            // Kein ForbidResult: das würde HttpContext.ForbidAsync() aufrufen, was einen
            // registrierten IAuthenticationService voraussetzt. Die Module nutzen aber kein
            // ASP.NET-Core-Authentifizierungsschema (JWT wird per eigener Middleware geparst),
            // daher würde ForbidResult mit 500 statt 403 fehlschlagen.
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
