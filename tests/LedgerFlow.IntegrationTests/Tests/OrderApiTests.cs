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

		// Привязываем логгер текущего теста к фабрике перед его запуском
		_factory.OutputHelper = outputHelper;

		_client = _factory.CreateClient();
		_faker = new Faker("ru");
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
}