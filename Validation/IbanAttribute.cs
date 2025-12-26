using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace InvoiceApp.Validation;

public class IbanAttribute : ValidationAttribute
{
    private static readonly Regex IbanRegex = new Regex(@"^DE\d{20}$", RegexOptions.Compiled);

    public IbanAttribute()
    {
        ErrorMessage = "Ungültige IBAN (Format: DE + 20 Ziffern)";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return ValidationResult.Success;
        }

        var iban = value.ToString()!.Replace(" ", "").ToUpper();

        if (!IbanRegex.IsMatch(iban))
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }
}
