using InventoryService.Application.Ports;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryService.Infrastructure.Persistence;

public sealed class EfCoreTransactionRunner : ITransactionRunner
{
    private readonly InventoryDbContext _dbContext;

    public EfCoreTransactionRunner(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async ct =>
        {
            await operation(ct);
            return true;
        }, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        await using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
