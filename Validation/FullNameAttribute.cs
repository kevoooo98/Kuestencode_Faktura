using System.ComponentModel.DataAnnotations;

namespace InvoiceApp.Validation;

public class FullNameAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string fullName)
        {
            return ValidationResult.Success;
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return ValidationResult.Success;
        }

        // Check if the name contains at least one space (indicates first and last name)
        if (!fullName.Trim().Contains(' '))
        {
            return new ValidationResult("Bitte geben Sie Vor- und Nachnamen ein");
        }

        return ValidationResult.Success;
    }
}
