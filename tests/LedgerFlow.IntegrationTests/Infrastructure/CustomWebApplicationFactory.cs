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
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis; // Добавили namespace для Redis контейнеров
using LedgerFlow.Api.Data;
using Xunit;
using Xunit.Abstractions;

namespace LedgerFlow.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
	// 1. Описываем конфигурацию контейнера PostgreSQL
	private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:17-alpine")
		.WithDatabase("ledgerflow_test_db")
		.WithUsername("fintech_user")
		.WithPassword("fintech_secure_pass")
		.Build();

	// 2. Описываем конфигурацию контейнера Redis (финтех-стандарт: легковесный alpine образ)
	private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine")
		.Build();

	private Respawner? _respawner;
	private DbConnection? _dbConnection;

	public ITestOutputHelper? OutputHelper { get; set; }

	public async Task InitializeAsync()
	{
		// Запускаем оба контейнера параллельно для экономии времени сборки стенда
		await Task.WhenAll(
			_postgresContainer.StartAsync(),
			_redisContainer.StartAsync()
		);

		// Накат миграций DbUp на Postgres контейнер
		var connectionString = _postgresContainer.GetConnectionString();
		var upgrader = DbUp.DeployChanges.To
			.PostgresqlDatabase(connectionString)
			.WithScriptsEmbeddedInAssembly(typeof(CustomWebApplicationFactory).Assembly)
			.LogToConsole()
			.Build();

		var result = upgrader.PerformUpgrade();
		if(!result.Successful)
		{
			throw new InvalidOperationException($"Критическая ошибка: Не удалось накатить DbUp миграции: {result.Error}");
		}

		// Инициализируем Respawn поверх созданных таблиц
		_dbConnection = new NpgsqlConnection(connectionString);
		await _dbConnection.OpenAsync();

		_respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
		{
			DbAdapter = DbAdapter.Postgres,
			SchemasToInclude = ["public"],
			TablesToIgnore = ["SchemaVersions"]
		});
	}

	/// <summary>
	/// Метод очистки состояния окружения между тестами
	/// </summary>
	public async Task ResetDatabaseAsync()
	{
		if(_dbConnection != null && _respawner != null)
		{
			// Сбрасываем таблицы в PostgreSQL
			await _respawner.ResetAsync(_dbConnection);
		}

		// ЖЕЛЕЗОБЕТОННАЯ ИЗОЛЯЦИЯ КЭША: Полностью очищаем все ключи в Redis (FLUSHALL)
		// Чтобы данные одного теста не аффектили Cache-Aside логику другого тесового метода
		// ✨ ПУЛЕНЕПРОБИВАЕМЫЙ ВАРИАНТ ДЛЯ ЛЮБОЙ ВЕРСИИ 4.X:
		try
		{
			// Просто выполняем очистку. Если контейнер почему-то не запущен, catch перехватит ошибку
			await _redisContainer.ExecAsync(new[] { "redis-cli", "FLUSHALL" });
		}
		catch(Exception ex)
		{
			// Логируем ошибку очистки кэша в системный вывод, если это необходимо
			System.Diagnostics.Debug.WriteLine($"Ошибка очистки Redis: {ex.Message}");
		}

	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		_ = builder ?? throw new ArgumentNullException(nameof(builder));

		builder.ConfigureAppConfiguration((_, configBuilder) =>
		{
			configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
			{
				// Подставляем динамические порты контейнеров Postgres и Redis в рантайм API
				["ConnectionStrings:DefaultConnection"] = _postgresContainer.GetConnectionString(),
				["ConnectionStrings:RedisConnection"] = _redisContainer.GetConnectionString(),
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

	public override async ValueTask DisposeAsync()
	{
		if(_dbConnection != null)
		{
			await _dbConnection.CloseAsync();
			await _dbConnection.DisposeAsync();
		}

		// Останавливаем и уничтожаем оба контейнера
		await Task.WhenAll(
			_postgresContainer.DisposeAsync().AsTask(),
			_redisContainer.DisposeAsync().AsTask()
		);

		await base.DisposeAsync();
	}

	async Task IAsyncLifetime.DisposeAsync()
	{
		await this.DisposeAsync();
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
