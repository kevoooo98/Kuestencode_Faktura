using System.Security.Claims;
using Kuestencode.Werkbank.Host.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kuestencode.Werkbank.Host.Auth;

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

        // Kein Auth-Identity → Middleware hat Auth deaktiviert oder keinen Token gefunden
        if (user.Identity?.AuthenticationType == "NoAuth")
            return; // Auth disabled, alles erlaubt

        // Interne Modul-Kommunikation immer durchlassen
        if (user.Identity?.AuthenticationType == "InternalService")
            return;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var roleClaim = user.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(roleClaim) ||
            !Enum.TryParse<UserRole>(roleClaim, out var userRole) ||
            !AllowedRoles.Contains(userRole))
        {
            // Kein ForbidResult: das würde HttpContext.ForbidAsync() aufrufen, was einen
            // registrierten IAuthenticationService voraussetzt. Der Host nutzt aber kein
            // ASP.NET-Core-Authentifizierungsschema (JWT wird per eigener Middleware geparst),
            // daher würde ForbidResult mit 500 statt 403 fehlschlagen.
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
