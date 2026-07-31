using Microsoft.Extensions.Logging;

namespace LedgerFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Реализация логгера, записывающая каждую строчку лога приложения в отчет конкретного теста.
/// </summary>
public class XUnitLogger : ILogger
{
	private readonly CustomWebApplicationFactory _factory;
	private readonly string _categoryName;

	public XUnitLogger(CustomWebApplicationFactory factory, string categoryName)
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
			// Берем логгер именно того теста, который выполняется прямо сейчас
			_factory.OutputHelper?.WriteLine(logLine);
		}
		catch { }
	}
}