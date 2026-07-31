using LedgerFlow.Api.BackgroundServices;
using LedgerFlow.Api.Data;
using LedgerFlow.Api.Messaging;
using LedgerFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Находим блок подключения БД в Program.cs и переписываем его так:
if(builder.Configuration["UseInMemoryDb"] == "true")
{
	// Если в конфиге флаг тестов, используем InMemory
	builder.Services.AddDbContext<OrderDbContext>(options =>
	{
		options.UseInMemoryDatabase("LedgerFlow_InMemory_TestDb");

		// ЖЕЛЕЗОБЕТОННОЕ РЕШЕНИЕ: Игнорируем тот факт, что InMemory не умеет в транзакции
		options.ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning));
	});
}
else
{
	// На проде и при локальном запуске жестко используем PostgreSQL
	builder.Services.AddDbContext<OrderDbContext>(options =>
		options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
}
builder.Services.AddSingleton<IMessageBroker, FakeMessageBroker>();
builder.Services.AddHostedService<OutboxPublisherWorker>();

var app = builder.Build();

app.UseHttpsRedirection();

// 🟢 РУЧКА GET: Получение заказа (Кэш -> БД)
app.MapGet("/orders/{id:guid}", async (Guid id, OrderDbContext dbContext) =>
{
	// Имитация Cache-Aside (в Блоке 2 заменим на реальный IDistributedCache / Redis)
	// Сейчас имитируем, что в кэше пусто, и всегда идем в базу
	var order = await dbContext.Orders.FindAsync(id);

	if(order is null)
	{
		return Results.NotFound(new { Message = $"Order {id} not found" });
	}

	var response = new OrderResponse(order.Id, order.CustomerId, order.Amount, order.Status, order.CreatedAt);
	return Results.Ok(response);
});

// 🟡 РУЧКА POST: Создание заказа и Outbox-события в одной транзакции
app.MapPost("/orders", async (CreateOrderRequest request, OrderDbContext dbContext) =>
{
	if(request.Amount <= 0)
		return Results.BadRequest(new { Message = "Amount must be greater than zero" });

	// Имитируем получение User ID из JWT Claims (в Блоке 4 протестируем авторизацию по-настоящему)
	var customerId = "user-123-fintech";

	var order = new Order
	{
		Id = Guid.NewGuid(),
		CustomerId = customerId,
		Amount = request.Amount,
		Status = "Created"
	};

	var outboxMessage = new OutboxMessage
	{
		Id = Guid.NewGuid(),
		Type = "OrderCreated",
		Payload = JsonSerializer.Serialize(new { order.Id, order.Amount, order.CustomerId })
	};

	// Финтех-стандарт: Атомарная транзакция (БД + Outbox)
	await using var transaction = await dbContext.Database.BeginTransactionAsync();
	try
	{
		dbContext.Orders.Add(order);
		dbContext.OutboxMessages.Add(outboxMessage);

		await dbContext.SaveChangesAsync();
		await transaction.CommitAsync();
	}
	catch(Exception)
	{
		await transaction.RollbackAsync();
		return Results.StatusCode(500);
	}

	var response = new OrderResponse(order.Id, order.CustomerId, order.Amount, order.Status, order.CreatedAt);

	return Results.Created($"/orders/{order.Id}", response);
});

await app.RunAsync();

public partial class Program
{
}