using Microsoft.EntityFrameworkCore;
using LedgerFlow.Api.Data;
using LedgerFlow.Api.Messaging;

namespace LedgerFlow.Api.BackgroundServices;

public sealed class OutboxPublisherWorker : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<OutboxPublisherWorker> _logger;
	private const string OrderEventsTopic = "order-events";

	public OutboxPublisherWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherWorker> logger)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("Outbox Publisher Worker запущен.");

		while(!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using var scope = _scopeFactory.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
				var messageBroker = scope.ServiceProvider.GetRequiredService<IMessageBroker>();

				// 1. Выбираем пачку неотправленных сообщений
				var messages = await dbContext.OutboxMessages
					.Where(m => m.ProcessedAt == null)
					.OrderBy(m => m.OccurredOn)
					.Take(10)
					.ToListAsync(stoppingToken);

				if(messages.Any())
				{
					_logger.LogInformation("Найдено {Count} сообщений в Outbox для отправки.", messages.Count);
				}

				foreach(var message in messages)
				{
					// 2. Публикуем в брокер (Kafka)
					await messageBroker.PublishAsync(OrderEventsTopic, message.Id.ToString(), message.Payload);

					// 3. Помечаем как обработанное
					message.ProcessedAt = DateTime.UtcNow;
				}

				if(messages.Any())
				{
					await dbContext.SaveChangesAsync(stoppingToken);
				}
			}
			catch(Exception ex)
			{
				_logger.LogError(ex, "Ошибка при обработке Outbox сообщений.");
			}

			// Опрашиваем таблицу раз в 2 секунды
			await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
		}
	}
}