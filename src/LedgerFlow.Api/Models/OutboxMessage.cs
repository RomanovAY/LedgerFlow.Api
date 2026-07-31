namespace LedgerFlow.Api.Models;

public class OutboxMessage
{
	public Guid Id { get; set; }
	public string Type { get; set; } = string.Empty; // Например, "OrderCreated"
	public string Payload { get; set; } = string.Empty; // JSON-строка события
	public DateTime OccurredOn { get; set; } = DateTime.UtcNow;
	public DateTime? ProcessedAt { get; set; } // Null, пока воркер не отправит в Kafka
}