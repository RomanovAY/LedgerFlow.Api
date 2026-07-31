namespace LedgerFlow.Api.Messaging;

public interface IMessageBroker
{
	Task PublishAsync(string topic, string key, string payload);
}