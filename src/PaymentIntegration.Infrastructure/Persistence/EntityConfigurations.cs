using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentIntegration.Domain.Entities;
using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Infrastructure.Persistence;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customer");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Ignore(x => x.FullName);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasColumnType("decimal(12,2)");
        builder.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentType)
            .HasColumnName("payment_type")
            .HasConversion<string>()
            .HasColumnType("enum('OneTime','Recurring')")
            .HasMaxLength(20);
    }
}

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transaction");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.InvoiceNo).HasColumnName("invoice_no").HasMaxLength(50).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(12,2)");
        builder.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasColumnType("enum('Pending','Succeeded','Failed')")
            .HasMaxLength(20);
        builder.Property(x => x.PaymentChannel).HasColumnName("payment_channel").HasMaxLength(20);
        builder.Property(x => x.MaskedCardNo).HasColumnName("masked_card_no").HasMaxLength(30);
        builder.Property(x => x.ProviderRef).HasColumnName("provider_ref").HasMaxLength(50);
        builder.Property(x => x.RespCode).HasColumnName("resp_code").HasMaxLength(10);
        // Longer than 100 so a configuration error still fits.
        builder.Property(x => x.RespDesc).HasColumnName("resp_desc").HasMaxLength(255);
        builder.Property(x => x.PaymentToken).HasColumnName("payment_token").HasMaxLength(1000);
        builder.Property(x => x.CheckoutResponseJson).HasColumnName("checkout_response_json").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");
        builder.Property(x => x.PaidAt).HasColumnName("paid_at").HasColumnType("datetime");

        builder.HasIndex(x => x.InvoiceNo).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerCardConfiguration : IEntityTypeConfiguration<CustomerCard>
{
    public void Configure(EntityTypeBuilder<CustomerCard> builder)
    {
        builder.ToTable("customer_card");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        // Tokens from 2C2P can run past 100 characters.
        builder.Property(x => x.CustomerToken).HasColumnName("customer_token").HasMaxLength(255).IsRequired();
        builder.Property(x => x.MaskedCardNo).HasColumnName("masked_card_no").HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime");

        builder.HasIndex(x => new { x.CustomerId, x.CustomerToken }).IsUnique();

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
