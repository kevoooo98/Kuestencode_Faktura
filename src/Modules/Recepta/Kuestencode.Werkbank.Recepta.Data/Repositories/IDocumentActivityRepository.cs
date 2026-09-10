using Kuestencode.Werkbank.Recepta.Domain.Entities;

namespace Kuestencode.Werkbank.Recepta.Data.Repositories;

public interface IDocumentActivityRepository
{
    Task<IEnumerable<DocumentActivityLog>> GetRecentAsync(int count);
}
