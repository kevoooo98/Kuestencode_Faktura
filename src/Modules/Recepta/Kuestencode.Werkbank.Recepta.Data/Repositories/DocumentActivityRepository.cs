using Kuestencode.Werkbank.Recepta.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Werkbank.Recepta.Data.Repositories;

public class DocumentActivityRepository : IDocumentActivityRepository
{
    private readonly ReceptaDbContext _context;

    public DocumentActivityRepository(ReceptaDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<DocumentActivityLog>> GetRecentAsync(int count)
    {
        return await _context.DocumentActivityLogs
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToListAsync();
    }
}
