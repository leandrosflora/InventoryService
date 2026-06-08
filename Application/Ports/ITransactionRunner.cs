namespace InventoryService.Application.Ports;

public interface ITransactionRunner
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);

    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
