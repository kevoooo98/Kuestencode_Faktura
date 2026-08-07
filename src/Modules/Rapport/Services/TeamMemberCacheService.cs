using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;

namespace Kuestencode.Rapport.Services;

public class TeamMemberCacheService
{
    private readonly IHostApiClient _hostApiClient;
    private List<TeamMemberDto>? _cachedMembers;
    private DateTime _lastFetch = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public TeamMemberCacheService(IHostApiClient hostApiClient)
    {
        _hostApiClient = hostApiClient;
    }

    public async Task<List<TeamMemberDto>> GetActiveTeamMembersAsync()
    {
        if (_cachedMembers != null && _cachedMembers.Count > 0 && DateTime.UtcNow - _lastFetch < CacheDuration)
            return _cachedMembers;

        try
        {
            var members = await _hostApiClient.GetTeamMembersAsync();
            // Offline-Mitarbeiter immer einschließen – sie haben keinen Systemzugang
            // und werden vom Admin manuell erfasst, unabhängig vom IsActive-Status.
            var active = members.Where(m => m.IsActive || m.IsOffline).ToList();
            if (active.Count > 0)
            {
                _cachedMembers = active;
                _lastFetch = DateTime.UtcNow;
            }
            return active;
        }
        catch
        {
            return _cachedMembers ?? new List<TeamMemberDto>();
        }
    }

    public async Task<TeamMemberDto?> GetByIdAsync(Guid id)
    {
        var members = await GetActiveTeamMembersAsync();
        return members.FirstOrDefault(m => m.Id == id);
    }

    public async Task<bool> HasMultipleActiveMembersAsync()
    {
        var members = await GetActiveTeamMembersAsync();
        return members.Count > 1;
    }

    public void InvalidateCache()
    {
        _cachedMembers = null;
        _lastFetch = DateTime.MinValue;
    }
}
