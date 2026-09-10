namespace Kuestencode.Werkbank.Recepta.Services.Interfaces;

public interface IDocumentActivityService
{
    Task<IEnumerable<DocumentActivityDto>> GetRecentAsync(int count = 15);
}

public class DocumentActivityDto
{
    public string UserName { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
