using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Domain.Entities;

namespace PaymentIntegration.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<CustomerCard> CustomerCards => Set<CustomerCard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerCardConfiguration());
    }
}
