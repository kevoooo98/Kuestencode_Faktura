using FluentAssertions;
using Kuestencode.Werkbank.Offerte.Controllers;
using Kuestencode.Werkbank.Offerte.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Controllers;

public class AngebotControllerTests
{
    private readonly Mock<IOfferteDruckService> _druckService = new();

    private AngebotController CreateController() => new(_druckService.Object, Mock.Of<ILogger<AngebotController>>());

    [Fact]
    public async Task GetPdfForPrint_ErfolgreichesPdf_GibtHtmlMitBase64EmbedZurueck()
    {
        var angebotId = Guid.NewGuid();
        _druckService.Setup(d => d.DruckvorbereitungAsync(angebotId)).ReturnsAsync([1, 2, 3]);

        var result = await CreateController().GetPdfForPrint(angebotId);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content.Content.Should().Contain(Convert.ToBase64String([1, 2, 3]));
    }

    [Fact]
    public async Task GetPdfForPrint_DruckServiceWirftException_GibtStatus500Zurueck()
    {
        var angebotId = Guid.NewGuid();
        _druckService.Setup(d => d.DruckvorbereitungAsync(angebotId)).ThrowsAsync(new InvalidOperationException("Fehler"));

        var result = await CreateController().GetPdfForPrint(angebotId);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }
}
