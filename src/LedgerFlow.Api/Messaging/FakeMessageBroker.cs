namespace LedgerFlow.Api.Messaging;

// Временная реализация-заглушка для запуска приложения без Kafka
public sealed class FakeMessageBroker : IMessageBroker
{
	public Task PublishAsync(string topic, string key, string payload)
	{
		// Пока просто имитируем успешную отправку
		return Task.CompletedTask;
	}
}