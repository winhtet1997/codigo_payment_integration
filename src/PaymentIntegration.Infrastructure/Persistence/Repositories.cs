using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Common;
using PaymentIntegration.Application.Contracts;
using PaymentIntegration.Domain.Entities;
using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Infrastructure.Persistence;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    public CustomerRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.FirstName)
            .ThenBy(customer => customer.LastName)
            .Select(customer => new CustomerResponse(customer.Id, customer.FirstName, customer.LastName, customer.Email))
            .ToListAsync(cancellationToken);
    }

    public Task<Customer?> FindAsync(int id, CancellationToken cancellationToken) =>
        _db.Customers.AsNoTracking().FirstOrDefaultAsync(customer => customer.Id == id, cancellationToken);
}

public sealed class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;

    public ProductRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProductResponse>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.Products
            .AsNoTracking()
            .OrderBy(product => product.Id)
            .Select(product => new ProductResponse(
                product.Id,
                product.Name,
                product.Description,
                product.Price,
                product.Currency,
                product.PaymentType == PaymentType.Recurring ? "Recurring" : "OneTime"))
            .ToListAsync(cancellationToken);
    }

    public Task<Product?> FindAsync(int id, CancellationToken cancellationToken) =>
        _db.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
}

public sealed class CustomerCardRepository : ICustomerCardRepository
{
    private readonly AppDbContext _db;

    public CustomerCardRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SavedCardResponse>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        return await _db.CustomerCards
            .AsNoTracking()
            .Where(card => card.CustomerId == customerId)
            .OrderByDescending(card => card.CreatedAt)
            .Select(card => new SavedCardResponse(card.Id, card.MaskedCardNo, card.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<CustomerCard?> FindAsync(int id, CancellationToken cancellationToken) =>
        _db.CustomerCards.AsNoTracking().FirstOrDefaultAsync(card => card.Id == id, cancellationToken);

    public async Task<bool> SaveIfNewAsync(int customerId, string customerToken, string maskedCardNo, CancellationToken cancellationToken)
    {
        var alreadyThere = await _db.CustomerCards.AnyAsync(
            card => card.CustomerId == customerId && card.CustomerToken == customerToken,
            cancellationToken);

        if (alreadyThere)
        {
            return false;
        }

        var card = new CustomerCard
        {
            CustomerId = customerId,
            CustomerToken = customerToken,
            MaskedCardNo = TextLimits.Clip(maskedCardNo, 30) ?? "Saved card",
            CreatedAt = DateTime.UtcNow
        };

        _db.CustomerCards.Add(card);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // Two notifications arrived together. The unique index kept a single card.
            _db.Entry(card).State = EntityState.Detached;
            return false;
        }
    }

    internal static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly AppDbContext _db;

    public TransactionRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        _db.Transactions.FirstOrDefaultAsync(transaction => transaction.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<Transaction?> FindByInvoiceNoAsync(string invoiceNo, CancellationToken cancellationToken) =>
        _db.Transactions.AsNoTracking().FirstOrDefaultAsync(transaction => transaction.InvoiceNo == invoiceNo, cancellationToken);

    public async Task<TransactionResponse?> GetResponseAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        var row = await _db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.InvoiceNo == invoiceNo)
            .Select(transaction => new
            {
                transaction.InvoiceNo,
                transaction.Status,
                transaction.RespCode,
                transaction.RespDesc,
                transaction.Amount,
                transaction.Currency,
                ProductName = transaction.Product!.Name,
                transaction.Product.PaymentType,
                transaction.CustomerId,
                transaction.ProductId,
                transaction.PaymentChannel,
                transaction.MaskedCardNo,
                transaction.PaidAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new TransactionResponse(
            row.InvoiceNo,
            row.Status.ToString(),
            row.RespCode,
            row.RespDesc,
            row.Amount,
            row.Currency,
            row.ProductName,
            row.PaymentType.ToString(),
            row.CustomerId,
            row.ProductId,
            row.PaymentChannel,
            row.MaskedCardNo,
            row.PaidAt);
    }

    public async Task<bool> TryAddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        _db.Transactions.Add(transaction);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (CustomerCardRepository.IsDuplicate(exception))
        {
            _db.Entry(transaction).State = EntityState.Detached;
            return false;
        }
    }

    public async Task SaveCheckoutSnapshotAsync(
        long transactionId,
        string? paymentToken,
        string checkoutResponseJson,
        string? respCode,
        string? respDesc,
        CancellationToken cancellationToken)
    {
        // Inquiry and the notification finalize with their own UPDATE. Saving the
        // tracked entity would put the earlier Do Payment code back on the row.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE `transaction`
            SET `checkout_response_json` = {checkoutResponseJson},
                `payment_token` = CASE WHEN {paymentToken} IS NULL THEN `payment_token` ELSE {paymentToken} END,
                `resp_code` = CASE WHEN `status` = 'Pending' THEN {respCode} ELSE `resp_code` END,
                `resp_desc` = CASE WHEN `status` = 'Pending' THEN {respDesc} ELSE `resp_desc` END
            WHERE `id` = {transactionId}
            """, cancellationToken);

        var tracked = _db.ChangeTracker.Entries<Transaction>()
            .FirstOrDefault(entry => entry.Entity.Id == transactionId);
        if (tracked is not null)
        {
            tracked.State = EntityState.Detached;
        }
    }

    public async Task<bool> TryFinalizeAsync(
        string invoiceNo,
        string status,
        string? paymentChannel,
        string? maskedCardNo,
        string? providerRef,
        string? respCode,
        string? respDesc,
        DateTime? paidAt,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TransactionStatus>(status, out var parsed) || parsed == TransactionStatus.Pending)
        {
            throw new InvalidOperationException("A final status must be Succeeded or Failed.");
        }

        // Conditional update: a second notification finds zero pending rows and changes nothing.
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE `transaction`
            SET `status` = {status},
                `payment_channel` = {paymentChannel},
                `masked_card_no` = {maskedCardNo},
                `provider_ref` = {providerRef},
                `resp_code` = {respCode},
                `resp_desc` = {respDesc},
                `paid_at` = {paidAt}
            WHERE `invoice_no` = {invoiceNo} AND `status` = 'Pending'
            """, cancellationToken);

        return rows == 1;
    }
}
