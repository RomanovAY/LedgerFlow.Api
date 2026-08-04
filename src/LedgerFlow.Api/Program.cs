using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed; // Добавлен using для кэша
using LedgerFlow.Api.BackgroundServices;
using LedgerFlow.Api.Data;
using LedgerFlow.Api.Messaging;
using LedgerFlow.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Подключаем базу данных PostgreSQL
builder.Services.AddDbContext<OrderDbContext>(options =>
{
	options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// 2. Подключаем распределенный кэш Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
	options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
	options.InstanceName = "LedgerFlow_"; // Префикс для изоляции ключей в Redis
});

// Регистрация остальных сервисов
builder.Services.AddSingleton<IMessageBroker, FakeMessageBroker>();
builder.Services.AddHostedService<OutboxPublisherWorker>();

var app = builder.Build();

app.UseHttpsRedirection();

// 🟢 РУЧКА GET: Получение заказа по паттерну Cache-Aside (Кэш -> БД)
app.MapGet("/orders/{id:guid}", async (
	Guid id,
	OrderDbContext dbContext,
	IDistributedCache cache, // Внедряем интерфейс работы с кэшем
	ILogger<Program> logger) =>
{
	var cacheKey = $"order:{id}";

	// Шаг A: Пытаемся прочитать данные из Redis
	var cachedOrderJson = await cache.GetStringAsync(cacheKey);
	if(!string.IsNullOrEmpty(cachedOrderJson))
	{
		logger.LogInformation("--- [CACHE HIT] Заказ {Id} успешно извлечен из Redis. ---", id);
		var cachedResponse = JsonSerializer.Deserialize<OrderResponse>(cachedOrderJson);
		return Results.Ok(cachedResponse);
	}

	logger.LogInformation("--- [CACHE MISS] Заказ {Id} не найден в кэше. Идем в PostgreSQL. ---", id);

	// Шаг B: Если в кэше пусто (Cache Miss), идем в реляционную базу Postgres
	var order = await dbContext.Orders.FindAsync(id);
	if(order is null)
	{
		return Results.NotFound(new { Message = $"Order {id} not found" });
	}

	var response = new OrderResponse(order.Id, order.CustomerId, order.Amount, order.Status, order.CreatedAt);

	// Шаг C: Сериализуем и сохраняем копию заказа в Redis со временем жизни (TTL) 5 минут
	var cacheOptions = new DistributedCacheEntryOptions
	{
		AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
	};
	var orderJsonToCache = JsonSerializer.Serialize(response);
	await cache.SetStringAsync(cacheKey, orderJsonToCache, cacheOptions);

	return Results.Ok(response);
});

// 🟡 РУЧКА POST: Создание заказа и Outbox-события в одной транзакции
app.MapPost("/orders", async (CreateOrderRequest request, OrderDbContext dbContext) =>
{
	if(request.Amount <= 0)
	{
		return Results.BadRequest(new { Message = "Amount must be greater than zero" });
	}

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

	using var transaction = await dbContext.Database.BeginTransactionAsync();
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

public partial class Program { }