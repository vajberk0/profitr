using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data.Entities;

namespace Profitr.Api.Data;

public class ProfitrDbContext(DbContextOptions<ProfitrDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Dividend> Dividends => Set<Dividend>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<CachedFxRate> CachedFxRates => Set<CachedFxRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.GoogleSubjectId).IsUnique();
            e.Property(u => u.DisplayCurrency).HasMaxLength(3).HasDefaultValue("EUR");
        });

        modelBuilder.Entity<Portfolio>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.User).WithMany(u => u.Portfolios).HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.UserId, p.IsDefault });
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Portfolio).WithMany(p => p.Transactions).HasForeignKey(t => t.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.PortfolioId, t.Symbol });
            e.Property(t => t.Quantity).HasColumnType("decimal(18,8)");
            e.Property(t => t.PricePerUnit).HasColumnType("decimal(18,8)");
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(4);
            e.Property(t => t.AssetType).HasMaxLength(10);
            e.Property(t => t.NativeCurrency).HasMaxLength(3);
            e.Property(t => t.Symbol).HasMaxLength(20);
        });

        modelBuilder.Entity<Dividend>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasOne(d => d.Portfolio).WithMany(p => p.Dividends).HasForeignKey(d => d.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.PortfolioId, d.Symbol });
            e.Property(d => d.AmountPerShare).HasColumnType("decimal(18,8)");
            e.Property(d => d.NativeCurrency).HasMaxLength(3);
            e.Property(d => d.Symbol).HasMaxLength(20);
        });

        modelBuilder.Entity<CachedFxRate>(e =>
        {
            e.HasKey(r => new { r.BaseCurrency, r.QuoteCurrency, r.RateDate });
            e.Property(r => r.BaseCurrency).HasMaxLength(3);
            e.Property(r => r.QuoteCurrency).HasMaxLength(3);
            e.Property(r => r.Rate).HasColumnType("decimal(18,8)");
        });

        modelBuilder.Entity<CashTransaction>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasOne(c => c.Portfolio).WithMany(p => p.CashTransactions).HasForeignKey(c => c.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.PortfolioId);
            e.Property(c => c.Amount).HasColumnType("decimal(18,8)");
            e.Property(c => c.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(c => c.Currency).HasMaxLength(3);
        });
    }
}
