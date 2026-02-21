using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// ──────────────────────────────────────────────────────────────────────────────
// Domain / Business Entities
// ──────────────────────────────────────────────────────────────────────────────

public class Order
{
    public string OrderId { get; set; } = Guid.NewGuid().ToString("N");
    public decimal TotalAmount { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public bool StockReserved { get; set; }
    public bool BalanceDeducted { get; set; }
    public bool LogisticsCreated { get; set; }
    public bool NotificationSent { get; set; }
    public string Status { get; set; } = "Pending";
}

// ──────────────────────────────────────────────────────────────────────────────
// Step Abstraction
// ──────────────────────────────────────────────────────────────────────────────

public interface ISagaStep
{
    string Name { get; }
    Task ExecuteAsync(Order order);
    Task CompensateAsync(Order order);
}

// ──────────────────────────────────────────────────────────────────────────────
// Concrete Saga Steps
// ──────────────────────────────────────────────────────────────────────────────

public class CreateOrderStep : ISagaStep
{
    public string Name => "Create Order";

    public Task ExecuteAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Creating order {order.OrderId} ...");
        // Simulate DB insert
        Task.Delay(80).Wait();
        Console.WriteLine($"[{Name}] Order created.");
        return Task.CompletedTask;
    }

    public Task CompensateAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Rolling back order creation for {order.OrderId} ...");
        Task.Delay(50).Wait();
        Console.WriteLine($"[{Name}] Order creation rolled back.");
        return Task.CompletedTask;
    }
}

public class DeductStockStep : ISagaStep
{
    public string Name => "Deduct Stock";

    public Task ExecuteAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Reserving stock for order {order.OrderId} ...");
        Task.Delay(120).Wait();

         order.StockReserved = true;
        Console.WriteLine($"[{Name}] Stock reserved.");
        return Task.CompletedTask;
    }

    public Task CompensateAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Releasing stock for order {order.OrderId} ...");
        order.StockReserved = false;
        Task.Delay(60).Wait();
        Console.WriteLine($"[{Name}] Stock released.");
        return Task.CompletedTask;
    }
}

public class DeductBalanceStep : ISagaStep
{
    public string Name => "Deduct Balance";

    public Task ExecuteAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Deducting {order.TotalAmount:C} from customer balance ...");
        Task.Delay(100).Wait();
       
        if (DateTime.Now.Millisecond % 7 == 0)
            throw new Exception("Payment gateway timeout");

        order.BalanceDeducted = true;
        Console.WriteLine($"[{Name}] Balance deducted.");
        return Task.CompletedTask;
    }

    public Task CompensateAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Refunding {order.TotalAmount:C} to customer ...");
        order.BalanceDeducted = false;
        Task.Delay(80).Wait();
        Console.WriteLine($"[{Name}] Refund completed.");
        return Task.CompletedTask;
    }
}

public class CreateLogisticsStep : ISagaStep
{
    public string Name => "Create Logistics";

    public Task ExecuteAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Creating shipping label for {order.OrderId} ...");
        Task.Delay(150).Wait();
        order.LogisticsCreated = true;
        Console.WriteLine($"[{Name}] Logistics created.");
        return Task.CompletedTask;
    }

    public Task CompensateAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Cancelling logistics for {order.OrderId} ...");
        order.LogisticsCreated = false;
        Task.Delay(70).Wait();
        Console.WriteLine($"[{Name}] Logistics cancelled.");
        return Task.CompletedTask;
    }
}

public class SendNotificationStep : ISagaStep
{
    public string Name => "Send Notification";

    public Task ExecuteAsync(Order order)
    {
        Console.WriteLine($"[{Name}] Sending order confirmation email/SMS ...");
        Task.Delay(60).Wait();
        order.NotificationSent = true;
        order.Status = "Confirmed";
        Console.WriteLine($"[{Name}] Notification sent.");
        return Task.CompletedTask;
    }

    public Task CompensateAsync(Order order)
    {
    
        Console.WriteLine($"[{Name}] No compensation needed for notification.");
        return Task.CompletedTask;
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Saga Orchestrator
// ──────────────────────────────────────────────────────────────────────────────

public class OrderCreationSaga
{
    private readonly List<ISagaStep> _allSteps;

    public OrderCreationSaga()
    {
        _allSteps = new List<ISagaStep>
        {
            new CreateOrderStep(),
            new DeductStockStep(),
            new DeductBalanceStep(),
            new CreateLogisticsStep(),
            new SendNotificationStep()
        };
    }

    public async Task ExecuteAsync(Order order)
    {
        // used LinkedList to track successfully completed steps
        var completedSteps = new LinkedList<ISagaStep>();

        try
        {
            foreach (var step in _allSteps)
            {
                await step.ExecuteAsync(order);
                completedSteps.AddLast(step);           // O(1) append
                Console.WriteLine($"→ Step completed: {step.Name}\n");
            }

            Console.WriteLine("Saga completed successfully!");
            Console.WriteLine($"Order {order.OrderId} is now CONFIRMED.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: {ex.Message}");
            Console.WriteLine("Starting compensation (rollback)...\n");

            // Compensate in reverse order
            while (completedSteps.Count > 0)
            {
                var lastNode = completedSteps.Last;           // O(1)
                var step = lastNode.Value;

                try
                {
                    await step.CompensateAsync(order);
                    Console.WriteLine($"→ Compensated: {step.Name}\n");
                }
                catch (Exception compEx)
                {
                    Console.WriteLine($"Compensation failed for {step.Name}: {compEx.Message}");
                    // In production: log, send to dead-letter, alert, etc.
                }

                completedSteps.RemoveLast();                  // O(1)
            }

            Console.WriteLine("Compensation finished. Order remains in inconsistent state.");
            throw; // rethrow or handle as business requires
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Demo / Main
// ──────────────────────────────────────────────────────────────────────────────

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Order Creation Saga Demo ===\n");

        var saga = new OrderCreationSaga();
        var order = new Order
        {
            CustomerId = "CUST-12345",
            TotalAmount = 299.99m
        };

        try
        {
            await saga.ExecuteAsync(order);
        }
        catch
        {
            Console.WriteLine("\nSaga execution failed (as designed in some runs).");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}


//dotnet run --project MyConsoleApp\MyConsoleApp.csproj