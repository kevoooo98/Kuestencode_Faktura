using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Kuestencode.Faktura.Validation;
using Xunit;

namespace Kuestencode.Faktura.Tests.Validation;

public class ValidationAttributesTests
{
    [Theory]
    [InlineData("K00001", true)]
    [InlineData("K12345", true)]
    [InlineData("K99999", true)]
    [InlineData("K0001", false)] // Zu kurz
    [InlineData("K000001", false)] // Zu lang
    [InlineData("A00001", false)] // Falsches Prefix
    [InlineData("00001", false)] // Kein Prefix
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CustomerNumberAttribute_ValidatesCorrectly(string? number, bool expected)
    {
        var attribute = new CustomerNumberAttribute();
        var context = new ValidationContext(new object());

        var result = attribute.GetValidationResult(number, context);

        (result == ValidationResult.Success).Should().Be(expected);
    }

    [Theory]
    [InlineData("12345", true)]
    [InlineData("00001", true)]
    [InlineData("1234", false)] // Zu kurz
    [InlineData("123456", false)] // Zu lang
    [InlineData("ABCDE", false)] // Nicht numerisch
    [InlineData("", true)] // Optional
    [InlineData(null, true)]
    public void GermanPostalCodeAttribute_ValidatesCorrectly(string? postalCode, bool expected)
    {
        var attribute = new GermanPostalCodeAttribute();
        var context = new ValidationContext(new object());

        var result = attribute.GetValidationResult(postalCode, context);

        (result == ValidationResult.Success).Should().Be(expected);
    }

    [Theory]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("DE89 3704 0044 0532 0130 00", true)] // Leerzeichen werden entfernt
    [InlineData("DE1234567890123456789", false)] // Zu kurz
    [InlineData("FR89370400440532013000", false)] // Falsches Land
    [InlineData("", true)] // Optional
    [InlineData(null, true)]
    public void IbanAttribute_ValidatesCorrectly(string? iban, bool expected)
    {
        var attribute = new IbanAttribute();
        var context = new ValidationContext(new object());

        var result = attribute.GetValidationResult(iban, context);

        (result == ValidationResult.Success).Should().Be(expected);
    }

    [Theory]
    [InlineData("Max Mustermann", true)]
    [InlineData("Anna Maria Schmidt", true)]
    [InlineData("Mustermann", false)] // Kein Leerzeichen
    [InlineData("", true)] // Optional
    [InlineData(null, true)]
    public void FullNameAttribute_ValidatesCorrectly(string? name, bool expected)
    {
        var attribute = new FullNameAttribute();
        var context = new ValidationContext(new object());

        var result = attribute.GetValidationResult(name, context);

        (result == ValidationResult.Success).Should().Be(expected);
    }
}
