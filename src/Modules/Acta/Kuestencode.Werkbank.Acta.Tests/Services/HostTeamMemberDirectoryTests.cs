using FluentAssertions;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Werkbank.Acta.Services;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Services;

public class HostTeamMemberDirectoryTests
{
    private readonly Mock<IHostApiClient> _hostApiClient = new();

    private HostTeamMemberDirectory CreateDirectory() => new(_hostApiClient.Object);

    // ─── GetAllAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_MapptUndSortiertNachDisplayName()
    {
        _hostApiClient.Setup(c => c.GetTeamMembersAsync()).ReturnsAsync(
        [
            new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "Zoe Zimmermann" },
            new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "Anna Adler" }
        ]);

        var result = await CreateDirectory().GetAllAsync();

        result.Should().HaveCount(2);
        result[0].DisplayName.Should().Be("Anna Adler");
        result[1].DisplayName.Should().Be("Zoe Zimmermann");
    }

    [Fact]
    public async Task GetAllAsync_FiltertMitgliederOhneDisplayNameHeraus()
    {
        _hostApiClient.Setup(c => c.GetTeamMembersAsync()).ReturnsAsync(
        [
            new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "" },
            new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "   " },
            new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "Max Mustermann" }
        ]);

        var result = await CreateDirectory().GetAllAsync();

        result.Should().ContainSingle();
        result[0].DisplayName.Should().Be("Max Mustermann");
    }

    [Fact]
    public async Task GetAllAsync_TrimmtDisplayNameUndErsetztLeereGuid()
    {
        _hostApiClient.Setup(c => c.GetTeamMembersAsync()).ReturnsAsync(
        [
            new TeamMemberDto { Id = Guid.Empty, DisplayName = "  Max Mustermann  " }
        ]);

        var result = await CreateDirectory().GetAllAsync();

        result[0].Id.Should().NotBe(Guid.Empty);
        result[0].DisplayName.Should().Be("Max Mustermann");
    }

    [Fact]
    public async Task GetAllAsync_ZweiterAufruf_VerwendetCacheOhneErneutenApiAufruf()
    {
        _hostApiClient.Setup(c => c.GetTeamMembersAsync()).ReturnsAsync([new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "Max" }]);
        var directory = CreateDirectory();

        await directory.GetAllAsync();
        await directory.GetAllAsync();

        _hostApiClient.Verify(c => c.GetTeamMembersAsync(), Times.Once);
    }

    // ─── GetByIdAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_VorhandeneId_GibtMitgliedZurueck()
    {
        var id = Guid.NewGuid();
        _hostApiClient.Setup(c => c.GetTeamMembersAsync()).ReturnsAsync([new TeamMemberDto { Id = id, DisplayName = "Max" }]);

        var result = await CreateDirectory().GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetByIdAsync_UnbekannteId_GibtNullZurueck()
    {
        _hostApiClient.Setup(c => c.GetTeamMembersAsync()).ReturnsAsync([new TeamMemberDto { Id = Guid.NewGuid(), DisplayName = "Max" }]);

        var result = await CreateDirectory().GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
