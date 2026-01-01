using System.ComponentModel.DataAnnotations;

namespace InvoiceApp.Services;

public class EmailValidationService : IEmailValidationService
{
    public (bool isValid, List<string> validEmails, List<string> errors) ValidateEmailList(string? emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            return (true, new List<string>(), new List<string>());

        var emails = emailString
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        var validEmails = new List<string>();
        var errors = new List<string>();
        var emailAttribute = new EmailAddressAttribute();

        foreach (var email in emails)
        {
            if (emailAttribute.IsValid(email))
            {
                validEmails.Add(email);
            }
            else
            {
                errors.Add($"Ungültige E-Mail-Adresse: {email}");
            }
        }

        return (errors.Count == 0, validEmails, errors);
    }
}
