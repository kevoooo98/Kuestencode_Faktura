namespace Kuestencode.Shared.Contracts.Faktura;

public record AuditLogEntryDto
{
    public string Action { get; init; } = string.Empty;
    public string? FieldName { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string ChangedByUserName { get; init; } = string.Empty;
    public DateTime ChangedAt { get; init; }
}
