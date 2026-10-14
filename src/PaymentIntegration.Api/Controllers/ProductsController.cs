using Microsoft.AspNetCore.Mvc;
using PaymentIntegration.Application.Catalog;
using PaymentIntegration.Application.Contracts;

namespace PaymentIntegration.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly CatalogService _catalog;

    public ProductsController(CatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Lists products.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var products = await _catalog.GetProductsAsync(cancellationToken);
        return Ok(products);
    }
}
