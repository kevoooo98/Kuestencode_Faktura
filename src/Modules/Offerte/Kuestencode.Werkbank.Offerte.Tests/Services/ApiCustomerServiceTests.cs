using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Werkbank.Offerte.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Services;

public class ApiCustomerServiceTests
{
    private readonly Mock<IHostApiClient> _hostApiClient = new();

    private ApiCustomerService CreateService() => new(_hostApiClient.Object, Mock.Of<ILogger<ApiCustomerService>>());

    // ─── GetAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_MapptAlleKundenKorrekt()
    {
        _hostApiClient.Setup(c => c.GetAllCustomersAsync()).ReturnsAsync(
        [
            new CustomerDto { Id = 1, Name = "Kunde A", Salutation = "Sehr geehrter Herr Kunde," },
            new CustomerDto { Id = 2, Name = "Kunde B" }
        ]);

        var result = await CreateService().GetAllAsync();

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Kunde A");
        result[0].Salutation.Should().Be("Sehr geehrter Herr Kunde,");
    }

    [Fact]
    public async Task GetAllAsync_HostApiWirftException_WirdWeitergegeben()
    {
        _hostApiClient.Setup(c => c.GetAllCustomersAsync()).ThrowsAsync(new HttpRequestException("down"));

        var act = () => CreateService().GetAllAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ─── GetByIdAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_VorhandenerKunde_GibtGemapptenKundenZurueck()
    {
        _hostApiClient.Setup(c => c.GetCustomerAsync(5)).ReturnsAsync(new CustomerDto { Id = 5, Name = "Kunde C" });

        var result = await CreateService().GetByIdAsync(5);

        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.Name.Should().Be("Kunde C");
    }

    [Fact]
    public async Task GetByIdAsync_UnbekannterKunde_GibtNullZurueck()
    {
        _hostApiClient.Setup(c => c.GetCustomerAsync(It.IsAny<int>())).ReturnsAsync((CustomerDto?)null);

        (await CreateService().GetByIdAsync(999)).Should().BeNull();
    }

    // ─── Im Microservice-Modus nicht unterstützte Operationen ──────────────

    [Fact]
    public async Task CreateAsync_WirftNotSupportedException()
    {
        var act = () => CreateService().CreateAsync(new Customer());

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task UpdateAsync_WirftNotSupportedException()
    {
        var act = () => CreateService().UpdateAsync(new Customer());

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task DeleteAsync_WirftNotSupportedException()
    {
        var act = () => CreateService().DeleteAsync(1);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task GenerateCustomerNumberAsync_WirftNotSupportedException()
    {
        var act = () => CreateService().GenerateCustomerNumberAsync();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task CustomerNumberExistsAsync_WirftNotSupportedException()
    {
        var act = () => CreateService().CustomerNumberExistsAsync("K-1");

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
