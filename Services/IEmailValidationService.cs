namespace InvoiceApp.Services;

public interface IEmailValidationService
{
    (bool isValid, List<string> validEmails, List<string> errors) ValidateEmailList(string? emailString);
}
