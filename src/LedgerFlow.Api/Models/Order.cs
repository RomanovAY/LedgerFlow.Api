
namespace LedgerFlow.Api.Models;

public class Order
{
	public Guid Id { get; set; }
	public string CustomerId { get; set; } = string.Empty;
	public decimal Amount { get; set; }
	public string Status { get; set; } = "Created"; // Created, Paid, Failed
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}