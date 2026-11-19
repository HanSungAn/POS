using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Data;

public class PosDbContext : DbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<SettlementRecord> SettlementRecords => Set<SettlementRecord>();
    public DbSet<SyncOutboxItem> SyncOutboxItems => Set<SyncOutboxItem>();
    public DbSet<DiagnosticLog> DiagnosticLogs => Set<DiagnosticLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Barcode).IsUnique();
            e.Property(x => x.Barcode).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.Price).HasColumnType("decimal(18,0)");
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.OrderNumber).HasMaxLength(32).IsRequired();
            e.Property(x => x.StoreCode).HasMaxLength(20);
            e.Property(x => x.PosNo).HasMaxLength(20);
            e.Property(x => x.TotalAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.SupplyAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.VatAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.DiscountAmount).HasColumnType("decimal(18,0)");
            e.Property(x => x.PaidAmount).HasColumnType("decimal(18,0)");

            e.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Payments)
                .WithOne()
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(100);
            e.Property(x => x.Barcode).HasMaxLength(32);
            e.Property(x => x.UnitPrice).HasColumnType("decimal(18,0)");
            e.Property(x => x.DiscountAmount).HasColumnType("decimal(18,0)");
            e.Ignore(x => x.LineTotal);
        });

        modelBuilder.Entity<PaymentTransaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TransactedAt);
            e.Property(x => x.Amount).HasColumnType("decimal(18,0)");
            e.Property(x => x.ApprovalNumber).HasMaxLength(32);
            e.Property(x => x.CardMaskedNumber).HasMaxLength(32);
            e.Property(x => x.CardIssuer).HasMaxLength(50);
            e.Property(x => x.VanCode).HasMaxLength(32);
        });

        modelBuilder.Entity<SettlementRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BusinessDate);
            e.Property(x => x.NetSales).HasColumnType("decimal(18,0)");
            e.Property(x => x.CardSales).HasColumnType("decimal(18,0)");
            e.Property(x => x.EasyPaySales).HasColumnType("decimal(18,0)");
            e.Property(x => x.CashSales).HasColumnType("decimal(18,0)");
            e.Property(x => x.ExpectedDrawerCash).HasColumnType("decimal(18,0)");
            e.Property(x => x.ActualDrawerCash).HasColumnType("decimal(18,0)");
            e.Ignore(x => x.CashDifference);
        });

        modelBuilder.Entity<SyncOutboxItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            e.Property(x => x.AggregateId).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<DiagnosticLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.Message).HasMaxLength(500);
        });
    }
}
