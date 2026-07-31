using LedgerFlow.Api.Data; // Добавили using
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore; // Добавили using
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection; // Добавили using
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LedgerFlow.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
	public ITestOutputHelper? OutputHelper { get; set; }

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Нам больше не нужно лезть в builder.ConfigureServices!
		// Мы просто элегантно меняем поведение Program.cs через конфигурацию:
		builder.ConfigureAppConfiguration((context, configBuilder) =>
		{
			configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["UseInMemoryDb"] = "true", // Включаем условный бранч в Program.cs
				["Features:UseMockServices"] = "true"
			});
		});

		// Блок логирования остается без изменений
		builder.ConfigureLogging(loggingBuilder =>
		{
			loggingBuilder.ClearProviders();
			loggingBuilder.SetMinimumLevel(LogLevel.Information);
			loggingBuilder.AddProvider(new XUnitLoggerProvider(this));
		});
	}
}