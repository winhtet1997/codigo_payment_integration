using Microsoft.AspNetCore.Mvc;
using PaymentIntegration.Application.Contracts;
using PaymentIntegration.Application.Payments;

namespace PaymentIntegration.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly TransactionQueryService _transactions;
    private readonly PaymentUpdateService _payments;

    public TransactionsController(TransactionQueryService transactions, PaymentUpdateService payments)
    {
        _transactions = transactions;
        _payments = payments;
    }

    /// <summary>
    /// Status for the result page. Always read from MySQL, never from a cache or from the browser redirect.
    /// </summary>
    [HttpGet("{invoiceNo}")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionResponse>> GetTransaction(string invoiceNo, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var transaction = await _transactions.GetAsync(invoiceNo, cancellationToken);
        return Ok(transaction);
    }

    /// <summary>
    /// Asks 2C2P with Payment Inquiry and then updates the row if it is still Pending.
    /// Saved-card payments use this after 3DS. Hosted payments use it when the notification cannot reach the API.
    /// </summary>
    [HttpPost("{invoiceNo}/inquiry")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionResponse>> Inquire(string invoiceNo, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var transaction = await _payments.InquireAsync(invoiceNo, cancellationToken);
        return Ok(transaction);
    }
}
