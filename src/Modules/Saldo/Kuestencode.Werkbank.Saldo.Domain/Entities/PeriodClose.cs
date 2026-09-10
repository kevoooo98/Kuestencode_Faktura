namespace Kuestencode.Werkbank.Saldo.Domain.Entities;

/// <summary>
/// Informativer Abschluss eines Zeitraums nach einem Export.
/// Blockiert keine Bearbeitung in Faktura/Recepta, dient nur als Hinweis
/// dass für diesen Zeitraum bereits Zahlen exportiert wurden.
/// </summary>
public class PeriodClose
{
    public Guid Id { get; set; }

    public DateOnly ZeitraumVon { get; set; }
    public DateOnly ZeitraumBis { get; set; }

    public DateTime ClosedAt { get; set; }
    public Guid ClosedByUserId { get; set; }

    /// <summary>Optionaler Bezug zum Export, der zum Abschluss geführt hat.</summary>
    public Guid? ExportLogId { get; set; }
}
