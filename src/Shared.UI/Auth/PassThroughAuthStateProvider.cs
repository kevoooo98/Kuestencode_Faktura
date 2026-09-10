using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace Kuestencode.Shared.UI.Auth;

/// <summary>
/// AuthenticationStateProvider for modules behind YARP reverse proxy.
/// Prerender: reads JWT from cookie/Authorization header via HttpContext.
/// SignalR circuit: reads JWT from window.__werkbank_jwt via JS-Interop.
/// </summary>
public class PassThroughAuthStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<PassThroughAuthStateProvider> _logger;

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    // Populated during Prerender so the circuit can skip JS-Interop if already resolved
    private AuthenticationState? _resolved;

    public PassThroughAuthStateProvider(
        IHttpContextAccessor httpContextAccessor,
        IJSRuntime jsRuntime,
        ILogger<PassThroughAuthStateProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Already resolved (Prerender ran first in this scope — unlikely for Server but safe)
        if (_resolved != null)
            return _resolved;

        // 1. Try HttpContext (Prerender / direct HTTP request)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var token = JwtPrincipalParser.ExtractToken(httpContext);
            if (!string.IsNullOrEmpty(token))
            {
                var principal = JwtPrincipalParser.Parse(token);
                if (principal != null)
                {
                    _logger.LogInformation("PassThrough: JWT from HttpContext. Role={Role}",
                        principal.FindFirstValue(ClaimTypes.Role));
                    _resolved = new AuthenticationState(principal);
                    return _resolved;
                }
            }

            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                _resolved = new AuthenticationState(httpContext.User);
                return _resolved;
            }
        }

        // 2. SignalR circuit: read from window.__werkbank_jwt set by _Host.cshtml
        try
        {
            var jwt = await _jsRuntime.InvokeAsync<string?>("getWerkbankJwt");
            if (!string.IsNullOrEmpty(jwt))
            {
                var principal = JwtPrincipalParser.Parse(jwt);
                if (principal != null)
                {
                    _logger.LogInformation("PassThrough: JWT from JS window.__werkbank_jwt. Role={Role}",
                        principal.FindFirstValue(ClaimTypes.Role));
                    _resolved = new AuthenticationState(principal);
                    return _resolved;
                }
            }
        }
        catch (Exception ex)
        {
            // JS-Interop not yet available (e.g. during prerender of a different component)
            _logger.LogDebug("PassThrough: JS-Interop not available: {Message}", ex.Message);
        }

        _logger.LogWarning("PassThrough: No auth found — returning anonymous");
        return new AuthenticationState(Anonymous);
    }
}
