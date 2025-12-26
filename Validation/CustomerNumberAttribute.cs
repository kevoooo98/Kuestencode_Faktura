using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace InvoiceApp.Validation;

public class CustomerNumberAttribute : ValidationAttribute
{
    private static readonly Regex CustomerNumberRegex = new Regex(@"^K\d{5}$", RegexOptions.Compiled);

    public CustomerNumberAttribute()
    {
        ErrorMessage = "Die Kundennummer muss im Format 'K00001' sein.";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return new ValidationResult("Kundennummer ist erforderlich.");
        }

        var customerNumber = value.ToString()!;

        if (!CustomerNumberRegex.IsMatch(customerNumber))
        {
            return new ValidationResult(ErrorMessage);
        }

        return ValidationResult.Success;
    }
}
