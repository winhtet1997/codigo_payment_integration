using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PaymentIntegration.Application.Catalog;

namespace PaymentIntegration.Api.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly CatalogService _catalog;

    public CustomersController(CatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Lists customers.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentIntegration.Application.Contracts.CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var customers = await _catalog.GetCustomersAsync(cancellationToken);
        return Ok(customers);
    }

    /// <summary>Saved cards for one customer. Returns the masked number only, never the token.</summary>
    [HttpGet("{id:int}/cards")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentIntegration.Application.Contracts.SavedCardResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cards([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var cards = await _catalog.GetCardsAsync(id, cancellationToken);
        return Ok(cards);
    }
}
