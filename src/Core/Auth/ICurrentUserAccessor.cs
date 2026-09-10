namespace Kuestencode.Core.Auth;

public record CurrentUser(Guid UserId, string UserName);

/// <summary>
/// Resolves who is currently making the request, for audit logging and export attribution.
/// </summary>
public interface ICurrentUserAccessor
{
    CurrentUser Get();
}
