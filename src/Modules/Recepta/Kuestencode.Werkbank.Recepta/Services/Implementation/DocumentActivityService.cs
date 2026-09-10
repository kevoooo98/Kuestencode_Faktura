using Kuestencode.Werkbank.Recepta.Data.Repositories;
using Kuestencode.Werkbank.Recepta.Services.Interfaces;

namespace Kuestencode.Werkbank.Recepta.Services.Implementation;

public class DocumentActivityService : IDocumentActivityService
{
    private readonly IDocumentActivityRepository _repository;

    public DocumentActivityService(IDocumentActivityRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<DocumentActivityDto>> GetRecentAsync(int count = 15)
    {
        var entries = await _repository.GetRecentAsync(count);
        return entries.Select(e => new DocumentActivityDto
        {
            UserName = e.UserName,
            DocumentNumber = e.DocumentNumber,
            Action = e.Action,
            CreatedAt = e.CreatedAt
        });
    }
}
