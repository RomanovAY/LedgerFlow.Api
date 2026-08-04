using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql; // Пакет для оркестрации Postgres в Docker
using LedgerFlow.Api.Data;
using Xunit.Abstractions;

namespace LedgerFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Кастомная фабрика, которая поднимает реальный PostgreSQL в Docker перед стартом приложения LedgerFlow.Api
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
	// Описываем конфигурацию нашего будущего Docker-контейнера
	private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
		.WithImage("postgres:17-alpine") // Официальный легковесный финтех-образ Postgres 17
		.WithDatabase("ledgerflow_test_db")
		.WithUsername("fintech_user")
		.WithPassword("fintech_secure_pass")
		.Build();

	public ITestOutputHelper? OutputHelper { get; set; }

	/// <summary>
	/// Интерфейс IAsyncLifetime: Метод запускается ДО старта TestServer и выполнения тестов
	/// </summary>
	public async Task InitializeAsync()
	{
		// 1. Посылаем команду Docker-демону скачать образ и поднять контейнер на случайном порту
		await _postgresContainer.StartAsync();

		// 2. Временный ручной мигратор (Этап 4): Создаем таблицы в только что поднятом контейнере
		var optionsBuilder = new DbContextOptionsBuilder<OrderDbContext>();
		optionsBuilder.UseNpgsql(_postgresContainer.GetConnectionString());

		using var dbContext = new OrderDbContext(optionsBuilder.Options);
		await dbContext.Database.EnsureCreatedAsync(); // Гарантирует создание таблиц Orders и OutboxMessages
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Перехватываем конфигурацию и подставляем динамическую строку подключения из Docker
		builder.ConfigureAppConfiguration((context, configBuilder) =>
		{
			configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
			{
				// Главная магия: переопределяем ConnectionString реальным адресом со случайным портом
				["ConnectionStrings:DefaultConnection"] = _postgresContainer.GetConnectionString(),
				["Features:UseMockServices"] = "true"
			});
		});

		// Блок логирования остается без изменений — он будет выводить SQL-логи от EF Core прямо в тест
		builder.ConfigureLogging(loggingBuilder =>
		{
			loggingBuilder.ClearProviders();
			loggingBuilder.SetMinimumLevel(LogLevel.Information);
			loggingBuilder.AddProvider(new XunitLoggerProvider(this));
		});
	}

	/// <summary>
	/// Интерфейс IAsyncLifetime: Метод запускается ПОСЛЕ прогона всех тестов в классе
	/// </summary>
	public async Task DisposeAsync()
	{
		// Безжалостно тушим и полностью удаляем контейнер из Docker, очищая за собой систему
		await _postgresContainer.DisposeAsync();
	}
}

#region Инфраструктура логирования для xUnit v2

public class XunitLoggerProvider : ILoggerProvider
{
	private readonly CustomWebApplicationFactory _factory;
	public XunitLoggerProvider(CustomWebApplicationFactory factory) => _factory = factory;
	public ILogger CreateLogger(string categoryName) => new XunitLogger(_factory, categoryName);
	public void Dispose() { }
}

public class XunitLogger : ILogger
{
	private readonly CustomWebApplicationFactory _factory;
	private readonly string _categoryName;

	public XunitLogger(CustomWebApplicationFactory factory, string categoryName)
	{
		_factory = factory;
		_categoryName = categoryName;
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		var message = formatter(state, exception);
		var logLine = $"[{DateTime.Now:HH:mm:ss}] [{logLevel.ToString().ToUpper()}] [{_categoryName}] {message}";

		if(exception != null) logLine += $"\n{exception}";

		try
		{
			_factory.OutputHelper?.WriteLine(logLine);
		}
		catch 
		{
		}
	}
}

#endregion
