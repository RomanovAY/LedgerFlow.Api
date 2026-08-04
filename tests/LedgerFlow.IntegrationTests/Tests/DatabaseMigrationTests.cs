using DbUp;
using FluentAssertions;
using LedgerFlow.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace LedgerFlow.IntegrationTests.Tests;

public class DatabaseMigrationTests : IClassFixture<CustomWebApplicationFactory>
{
	private readonly CustomWebApplicationFactory _factory;

	public DatabaseMigrationTests(CustomWebApplicationFactory factory, ITestOutputHelper outputHelper)
	{
		_factory = factory ?? throw new ArgumentNullException(nameof(factory));
		_factory.OutputHelper = outputHelper;
	}

	/// <summary>
	/// Тест проверяет, что повторный запуск мигратора DbUp на уже существующей базе данных
	/// завершается успешно и не падает из-за попыток пересоздать таблицы.
	/// </summary>
	[Fact]
	public void MigrateDatabase_RunIdempotently_ShouldReturnSuccessOnRepeatedExecution()
	{
		// Arrange
		// Извлекаем строку подключения к нашему запущенному контейнеру Postgres через DI фабрики
		var connectionString = _factory.Services
			.GetRequiredService<IConfiguration>()
			.GetConnectionString("DefaultConnection");

		connectionString.Should().NotBeNullOrEmpty("Фабрика должна была инициализировать строку подключения к Docker");

		// Настраиваем ВТОРОЙ (повторный) экземпляр мигратора DbUp
		var upgrader = DeployChanges.To
			.PostgresqlDatabase(connectionString)
			.WithScriptsEmbeddedInAssembly(typeof(CustomWebApplicationFactory).Assembly)
			.LogToConsole()
			.Build();

		// Act
		// Запускаем мигратор повторно на той же самой базе данных
		var result = upgrader.PerformUpgrade();

		// Assert
		// 1. Проверяем, что запуск прошел успешно (Success = true)
		result.Successful.Should().BeTrue($"Повторный накат миграций должен быть успешным. Ошибка: {result.Error}");

		// 2. ИСПРАВЛЕНО: Проверяем через свойство .Scripts, что количество примененных во второй раз скриптов строго равно 0
		result.Scripts.Should().HaveCount(0, "При повторном запуске мигратор не должен накатывать уже примененные скрипты");
	}
}