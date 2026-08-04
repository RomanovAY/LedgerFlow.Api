using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Respawn; // Добавлен обязательный using для версии 7.0.0
using Testcontainers.PostgreSql;
using LedgerFlow.Api.Data;
using Xunit;
using Xunit.Abstractions;

namespace LedgerFlow.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
	// ИСПРАВЛЕНО: Передаем образ прямо в конструктор согласно требованиям Testcontainers 4.x
	private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:17-alpine")
		.WithDatabase("ledgerflow_test_db")
		.WithUsername("fintech_user")
		.WithPassword("fintech_secure_pass")
		.Build();

	private Respawner? _respawner;
	private DbConnection? _dbConnection;

	public ITestOutputHelper? OutputHelper { get; set; }

	public async Task InitializeAsync()
	{
		// 1. Командуем Docker-демону запустить Postgres контейнер
		await _postgresContainer.StartAsync();

		// 2. Настраиваем и запускаем мигратор DbUp на базе нашего контейнера
		var connectionString = _postgresContainer.GetConnectionString();

		var upgrader = DbUp.DeployChanges.To
			.PostgresqlDatabase(connectionString)
			// Указываем DbUp искать встроенные .sql ресурсы в текущей сборке (Assembly) тестов
			.WithScriptsEmbeddedInAssembly(typeof(CustomWebApplicationFactory).Assembly)
			// Включаем красивое логирование миграций прямо в консоль сборщика
			.LogToConsole()
			.Build();

		// Запускаем процесс наката скриптов миграции
		var result = upgrader.PerformUpgrade();

		// Финтех-стандарт: Если миграция упала, тесты не должны запускаться вслепую
		if(!result.Successful)
		{
			throw new InvalidOperationException($"Критическая ошибка: Не удалось накатить DbUp миграции: {result.Error}");
		}

		// 3. Инициализируем Respawn для очистки данных поверх уже созданных таблиц
		_dbConnection = new NpgsqlConnection(connectionString);
		await _dbConnection.OpenAsync();

		_respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
		{
			DbAdapter = DbAdapter.Postgres,
			SchemasToInclude = ["public"],
			// Уникальная ценность: С этого момента мы ведем таблицу истории SchemaVersions!
			// Говорим Respawn ни в коем случае НЕ удалять таблицу истории миграций DbUp между тестами
			TablesToIgnore = ["SchemaVersions"]
		});
	}


	public async Task ResetDatabaseAsync()
	{
		if(_dbConnection != null && _respawner != null)
		{
			await _respawner.ResetAsync(_dbConnection);
		}
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Подавляем предупреждение о неиспользуемом параметре context
		_ = builder ?? throw new ArgumentNullException(nameof(builder));

		builder.ConfigureAppConfiguration((_, configBuilder) =>
		{
			configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["ConnectionStrings:DefaultConnection"] = _postgresContainer.GetConnectionString(),
				["Features:UseMockServices"] = "true"
			});
		});

		builder.ConfigureLogging(loggingBuilder =>
		{
			loggingBuilder.ClearProviders();
			loggingBuilder.SetMinimumLevel(LogLevel.Information);
			loggingBuilder.AddProvider(new XunitLoggerProvider(this));
		});
	}

	/// <summary>
	/// Переопределение метода очистки базового класса WebApplicationFactory (IAsyncDisposable)
	/// </summary>
	public override async ValueTask DisposeAsync()
	{
		if(_dbConnection != null)
		{
			await _dbConnection.CloseAsync();
			await _dbConnection.DisposeAsync();
		}
		await _postgresContainer.DisposeAsync();
		await base.DisposeAsync();
	}

	/// <summary>
	/// Явная реализация метода очистки интерфейса IAsyncLifetime для xUnit v2
	/// </summary>
	async Task IAsyncLifetime.DisposeAsync()
	{
		// Просто перенаправляем вызов в наш основной метод DisposeAsync
		await DisposeAsync();
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
		catch { }
	}
}

#endregion
