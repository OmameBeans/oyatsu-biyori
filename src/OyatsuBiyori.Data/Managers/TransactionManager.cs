using Microsoft.EntityFrameworkCore.Storage;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Data.Managers;

/// <summary>
/// DbContext のトランザクションで、複数の書き込みをまとめる。
/// </summary>
public sealed class TransactionManager(OyatsuBiyoriDbContext context) : ITransactionManager
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync()
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("トランザクションはすでに始まっています");
        }

        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitAsync()
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("トランザクションが始まっていません");
        }

        await _transaction.CommitAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
