
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions; // Изменился namespace логгера для v2

namespace LedgerFlow.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
	// Свойство для динамической подмены логгера текущего теста
	public ITestOutputHelper? OutputHelper { get; set; }

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.ConfigureAppConfiguration((context, configBuilder) =>
		{
			configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Features:UseMockServices"] = "true",
				["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=FakeTestDb"
			});
		});

		builder.ConfigureLogging(loggingBuilder =>
		{
			loggingBuilder.ClearProviders();
			loggingBuilder.SetMinimumLevel(LogLevel.Information);
			// Передаем фабрику, чтобы логгер всегда читал актуальный OutputHelper
			loggingBuilder.AddProvider(new XUnitLoggerProvider(this));
		});
	}
}