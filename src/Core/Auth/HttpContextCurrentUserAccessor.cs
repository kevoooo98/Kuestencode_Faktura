using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Kuestencode.Core.Auth;

public class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser Get()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var userIdClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = user?.FindFirst(ClaimTypes.Name)?.Value;

        if (userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
        {
            return new CurrentUser(userId, userName ?? "Unbekannt");
        }

        return new CurrentUser(Guid.Empty, "System");
    }
}
