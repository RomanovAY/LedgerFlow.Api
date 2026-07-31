namespace LedgerFlow.Api.Models;

// DTO для ответа эндпоинтов
public record OrderResponse(Guid Id, string CustomerId, decimal Amount, string Status, DateTime CreatedAt);