using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace InvoiceApp.Validation;

public class GermanPostalCodeAttribute : ValidationAttribute
{
    private static readonly Regex PostalCodeRegex = new Regex(@"^\d{5}$", RegexOptions.Compiled);

    public GermanPostalCodeAttribute()
    {
        ErrorMessage = "Die Postleitzahl muss aus 5 Ziffern bestehen.";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return ValidationResult.Success;
        }

        var postalCode = value.ToString()!;

        if (!PostalCodeRegex.IsMatch(postalCode))
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }
}
