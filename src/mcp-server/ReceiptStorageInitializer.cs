using Expenses.Data;

namespace ExpensesMcpServer;

public sealed class ReceiptStorageInitializer(ReceiptStorage receipts) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => receipts.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
