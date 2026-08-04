using System.Reflection;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerFlow.Migrations;

public static class MigrationExtensions
{
	public static IServiceCollection AddFluentMigrations(this IServiceCollection services, string connectionString)
	{
		return services
			.AddFluentMigratorCore()
			.ConfigureRunner(rb => rb
				.AddPostgres() // Используем диалект PostgreSQL
				.WithGlobalConnectionString(connectionString)
				.ScanIn(Assembly.GetExecutingAssembly()).For.Migrations()) // Сканируем текущую сборку
			.AddLogging(lb => lb.AddFluentMigratorConsole());
	}

	public static void RunMigrations(this IServiceProvider serviceProvider)
	{
		using var scope = serviceProvider.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

		// Накатываем все миграции до актуальной версии
		runner.MigrateUp();
	}
}
