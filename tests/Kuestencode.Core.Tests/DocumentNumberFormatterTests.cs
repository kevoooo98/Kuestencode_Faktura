using FluentAssertions;
using Kuestencode.Core.Services;
using Xunit;

namespace Kuestencode.Core.Tests;

public class DocumentNumberFormatterTests
{
    private static readonly DateTime ReferenceDate = new(2026, 3, 15);

    [Fact]
    public void GenerateNext_NoExistingNumbers_StartsAtOne()
    {
        var result = DocumentNumberFormatter.GenerateNext("YYYY-XXXX", ReferenceDate, []);

        result.Should().Be("2026-0001");
    }

    [Fact]
    public void GenerateNext_WithExistingNumbers_ContinuesSequence()
    {
        var existing = new[] { "2026-0001", "2026-0002", "2026-0005" };

        var result = DocumentNumberFormatter.GenerateNext("YYYY-XXXX", ReferenceDate, existing);

        result.Should().Be("2026-0006");
    }

    [Fact]
    public void GenerateNext_NumbersFromOtherYear_AreIgnored()
    {
        var existing = new[] { "2025-0099" };

        var result = DocumentNumberFormatter.GenerateNext("YYYY-XXXX", ReferenceDate, existing);

        result.Should().Be("2026-0001");
    }

    [Fact]
    public void GetLastSequenceNumber_ReturnsHighestMatchingNumber()
    {
        var existing = new[] { "2026-0001", "2026-0002", "2026-0005", "2025-0099" };

        var result = DocumentNumberFormatter.GetLastSequenceNumber("YYYY-XXXX", ReferenceDate, existing);

        result.Should().Be(5);
    }

    [Fact]
    public void GetLastSequenceNumber_NoExistingNumbers_ReturnsZero()
    {
        var result = DocumentNumberFormatter.GetLastSequenceNumber("YYYY-XXXX", ReferenceDate, []);

        result.Should().Be(0);
    }

    [Fact]
    public void Format_RendersGivenSequenceNumberWithoutScanning()
    {
        var result = DocumentNumberFormatter.Format("YYYY-XXXX", ReferenceDate, 7);

        result.Should().Be("2026-0007");
    }

    [Fact]
    public void Format_MatchesGenerateNext_ForEquivalentSequenceNumber()
    {
        var lastNumber = DocumentNumberFormatter.GetLastSequenceNumber("YY-XXXXX-RE", ReferenceDate, new[] { "26-00041-RE" });
        var viaFormat = DocumentNumberFormatter.Format("YY-XXXXX-RE", ReferenceDate, lastNumber + 1);
        var viaGenerateNext = DocumentNumberFormatter.GenerateNext("YY-XXXXX-RE", ReferenceDate, new[] { "26-00041-RE" });

        viaFormat.Should().Be(viaGenerateNext);
        viaFormat.Should().Be("26-00042-RE");
    }
}
