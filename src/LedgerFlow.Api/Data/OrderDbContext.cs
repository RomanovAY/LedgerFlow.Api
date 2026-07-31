using Microsoft.EntityFrameworkCore;
using LedgerFlow.Api.Models;

namespace LedgerFlow.Api.Data;

public sealed class OrderDbContext : DbContext
{
	public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
	{
	}

	public DbSet<Order> Orders => Set<Order>();
	public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Конфигурация для финтех-совместимости (строгие типы данных)
		modelBuilder.Entity<Order>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
		});

		modelBuilder.Entity<OutboxMessage>(entity => entity.HasKey(e => e.Id));
	}
}