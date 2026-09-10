namespace Kuestencode.Faktura.Models;

/// <summary>
/// Zählerstand für einen Nummernkreis (z.B. "Invoice:2026-"), atomar fortgeschrieben
/// über <see cref="Kuestencode.Core.Services.NumberSequenceService"/>.
/// </summary>
public class NumberSequence
{
    public Guid Id { get; set; }
    public string SequenceKey { get; set; } = string.Empty;
    public long CurrentValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
