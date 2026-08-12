using Kuestencode.Rapport.Services;

namespace Kuestencode.Rapport.IntegrationTests.TestDoubles;

public sealed class TestUserContextService : IUserContextService
{
    public Task<Guid?> GetCurrentUserIdAsync() => Task.FromResult<Guid?>(null);

    public Task<string?> GetCurrentUserNameAsync() => Task.FromResult<string?>("Test Admin");

    public Task<string?> GetCurrentUserRoleAsync() => Task.FromResult<string?>("Admin");

    public Task<bool> IsAdminOrBueroAsync() => Task.FromResult(true);

    public Task<bool> IsAdminAsync() => Task.FromResult(true);

    public Task<(int? RolleId, string? RolleName)> GetCurrentUserMitarbeiterRolleAsync() =>
        Task.FromResult<(int?, string?)>((null, null));
}
