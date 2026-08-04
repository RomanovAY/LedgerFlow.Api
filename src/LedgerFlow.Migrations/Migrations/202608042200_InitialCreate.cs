using FluentMigrator;

namespace LedgerFlow.Migrations.Migrations;

[Migration(202608042200)]
public sealed class InitialCreate : Migration
{
	public override void Up()
	{
		// Создание таблицы Orders
		Create.Table("Orders")
			.WithColumn("Id").AsGuid().PrimaryKey()
			.WithColumn("CustomerId").AsString(255).NotNullable()
			.WithColumn("Amount").AsDecimal(18, 2).NotNullable() // Финтех-совместимость
			.WithColumn("Status").AsString(50).NotNullable().WithDefaultValue("Created")
			.WithColumn("CreatedAt").AsDateTime2().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime);

		// Создание таблицы OutboxMessages
		Create.Table("OutboxMessages")
			.WithColumn("Id").AsGuid().PrimaryKey()
			.WithColumn("Type").AsString(255).NotNullable()
			.WithColumn("Payload").AsString(int.MaxValue).NotNullable()
			.WithColumn("OccurredOn").AsDateTime2().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime)
			.WithColumn("ProcessedAt").AsDateTime2().Nullable();

		// Индекс для оптимизации выборки воркером (OutboxPublisherWorker)
		Create.Index("IX_OutboxMessages_ProcessedAt_OccurredOn")
			.OnTable("OutboxMessages")
			.OnColumn("ProcessedAt").Ascending()
			.OnColumn("OccurredOn").Ascending();
	}

	public override void Down()
	{
		Delete.Index("IX_OutboxMessages_ProcessedAt_OccurredOn").OnTable("OutboxMessages");
		Delete.Table("OutboxMessages");
		Delete.Table("Orders");
	}
}
