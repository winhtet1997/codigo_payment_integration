using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PaymentIntegration.Application.Payments;

namespace PaymentIntegration.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentUpdateService _payments;

    public PaymentsController(PaymentUpdateService payments)
    {
        _payments = payments;
    }

    /// <summary>
    /// 2C2P backend notification (backendReturnUrl). The body is a JWT in the payload field.
    /// This endpoint checks the signature, checks the amount, and is safe to receive twice.
    /// </summary>
    [HttpPost("2c2p/notification")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Notification([FromBody] NotificationRequest request, CancellationToken cancellationToken)
    {
        await _payments.HandleNotificationAsync(request.Payload, cancellationToken);
        return Ok(new { received = true });
    }
}

public sealed class NotificationRequest
{
    /// <summary>JWT signed with the merchant secret. Example: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...</summary>
    [Required]
    public string Payload { get; set; } = "";
}
