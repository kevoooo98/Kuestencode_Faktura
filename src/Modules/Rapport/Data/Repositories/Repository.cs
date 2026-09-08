using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Rapport.Data.Repositories;

public class Repository<T> where T : class
{
    protected readonly IDbContextFactory<RapportDbContext> _contextFactory;

    public Repository(IDbContextFactory<RapportDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().FindAsync(id);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().ToListAsync();
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().Where(predicate).ToListAsync();
    }

    public virtual async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().FirstOrDefaultAsync(predicate);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Set<T>().AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public virtual async Task UpdateAsync(T entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Set<T>().Update(entity);
        await context.SaveChangesAsync();
    }

    public virtual async Task DeleteAsync(T entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Set<T>().Remove(entity);
        await context.SaveChangesAsync();
    }

    public virtual async Task<int> CountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().CountAsync();
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().CountAsync(predicate);
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Set<T>().AnyAsync(predicate);
    }
}
