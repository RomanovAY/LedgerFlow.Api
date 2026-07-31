using Microsoft.Extensions.Logging;

namespace LedgerFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Провайдер, связывающий ILogger из Microsoft.Extensions.Logging с ITestOutputHelper из xUnit v3.
/// </summary>
public class XUnitLoggerProvider : ILoggerProvider
{
	private readonly CustomWebApplicationFactory _factory;

	public XUnitLoggerProvider(CustomWebApplicationFactory factory)
	{
		_factory = factory;
	}

	public ILogger CreateLogger(string categoryName) => new XUnitLogger(_factory, categoryName);
	public void Dispose() { }
}