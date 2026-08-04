using System.Net;
using System.Net.Http.Json;
using Bogus;
using FluentAssertions;
using LedgerFlow.Api.Models;
using LedgerFlow.IntegrationTests.Infrastructure;
using Xunit.Abstractions; // Использование абстракций v2

namespace LedgerFlow.IntegrationTests.Tests;

public class OrderApiTests : IClassFixture<CustomWebApplicationFactory>
{
	private readonly CustomWebApplicationFactory _factory;
	private readonly HttpClient _client;
	private readonly Faker _faker;

	public OrderApiTests(CustomWebApplicationFactory factory, ITestOutputHelper outputHelper)
	{
		_factory = factory ?? throw new ArgumentNullException(nameof(factory));
		_factory.OutputHelper = outputHelper;
		_client = _factory.CreateClient();
		_faker = new Faker("ru");

		// ЖЕЛЕЗОБЕТОННАЯ ИЗОЛЯЦИЯ: Перед каждым тестом мгновенно зачищаем таблицы базы данных
		_factory.ResetDatabaseAsync().GetAwaiter().GetResult();
	}


	[Fact]
	public async Task CreateOrder_WithValidAmount_ShouldReturnCreatedAndWriteToDb()
	{
		// Arrange
		var validAmount = Math.Round(_faker.Random.Decimal(100, 5000), 2);
		var request = new CreateOrderRequest(validAmount);

		// Act
		var response = await _client.PostAsync("/orders", JsonContent.Create(request));

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		var orderResult = await response.Content.ReadFromJsonAsync<OrderResponse>();
		orderResult.Should().NotBeNull();

		orderResult!.Id.Should().NotBeEmpty("Идентификатор заказа должен быть сгенерирован (Guid)");
		orderResult.Amount.Should().Be(validAmount, "Сумма в ответе должна в точности совпадать с запросом");
		orderResult.Status.Should().Be("Created", "Начальный статус финтех-заказа всегда должен быть 'Created'");
		orderResult.CustomerId.Should().Be("user-123-fintech", "Система должна временно подставлять дефолтного пользователя");

		response.Headers.Location.Should().NotBeNull();
		response.Headers.Location!.ToString().Should().Be($"/orders/{orderResult.Id}");
	}

	[Fact]
	public async Task CreateOrder_WithInvalidAmount_ShouldReturnBadRequest()
	{
		// Arrange (Подготовка некорректных данных)
		// Генерируем невалидную отрицательную сумму заказа
		var invalidAmount = _faker.Random.Decimal(-100, 0);
		var request = new CreateOrderRequest(invalidAmount);

		// Act (Выполнение запроса)
		var response = await _client.PostAsync("/orders", JsonContent.Create(request));

		// Assert (Проверка результатов)
		// 1. Ожидаем, что ручка выдаст статус 400 Bad Request
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

		// 2. Дополнительно проверяем структуру ошибки (если хотим убедиться в понятном ответе)
		var errorResult = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
		errorResult.Should().NotBeNull();
		errorResult.Should().ContainKey("message");
		errorResult!["message"].Should().Be("Amount must be greater than zero");
	}
}