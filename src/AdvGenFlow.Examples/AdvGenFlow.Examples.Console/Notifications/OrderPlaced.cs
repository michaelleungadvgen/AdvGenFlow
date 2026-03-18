using AdvGenFlow;

namespace AdvGenFlow.Examples.ConsoleApp.Notifications;

// Notification
public record OrderPlaced(int OrderId) : INotification;

// Handler 1: Email notification
public class OrderEmailHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [EmailHandler] Sending confirmation email for order #{notification.OrderId}");
        return Task.CompletedTask;
    }
}

// Handler 2: Audit logging
public class OrderAuditHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [AuditHandler] Logging order #{notification.OrderId} to audit trail");
        return Task.CompletedTask;
    }
}

// Handler 3: Inventory update
public class OrderInventoryHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [InventoryHandler] Updating inventory for order #{notification.OrderId}");
        return Task.CompletedTask;
    }
}
