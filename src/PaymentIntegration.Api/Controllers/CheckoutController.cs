using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PaymentIntegration.Application.Checkout;
using PaymentIntegration.Application.Contracts;

namespace PaymentIntegration.Api.Controllers;

[ApiController]
[Route("api/checkout")]
public sealed class CheckoutController : ControllerBase
{
    private readonly CheckoutService _checkout;

    public CheckoutController(CheckoutService checkout)
    {
        _checkout = checkout;
    }

    /// <summary>
    /// Starts a payment. The amount is taken from the product, not from this body.
    /// The same Idempotency-Key returns the original invoice and redirect URL and does not call 2C2P again.
    /// </summary>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CheckoutResponse>> Checkout(
        [FromBody] CheckoutRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _checkout.CheckoutAsync(
            new CheckoutCommand(request.CustomerId, request.ProductId, request.CustomerCardId, idempotencyKey ?? ""),
            cancellationToken);

        return Ok(result);
    }
}

public sealed class CheckoutRequest
{
    /// <summary>Seeded customer id.</summary>
    [Range(1, int.MaxValue)]
    public int CustomerId { get; set; }

    /// <summary>Seeded product id. The server reads the price from this product.</summary>
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    /// <summary>Saved card id, or null to pay with a new payment method.</summary>
    public int? CustomerCardId { get; set; }
}
