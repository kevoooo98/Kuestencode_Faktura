using System.ComponentModel.DataAnnotations;

namespace Kuestencode.Faktura.Models;

public class InvoiceAttachment
{
    public int Id { get; set; }

    [Required]
    public int InvoiceId { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    [Required]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True für das beim Versand/Druck eingefrorene PDF (GoBD-Unveränderbarkeit) — wird nicht
    /// als zusätzlicher E-Mail-Anhang mitgeschickt, siehe <see cref="Email.EmailAttachmentBuilder"/>.
    /// </summary>
    public bool IsFrozenSnapshot { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
