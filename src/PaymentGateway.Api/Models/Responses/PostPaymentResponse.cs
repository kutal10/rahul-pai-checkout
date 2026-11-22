using PaymentGateway.Api.Models.Enums;

namespace PaymentGateway.Api.Models.Responses;

/// <summary>
/// Represents a payment and its authorization status.
/// </summary>
public sealed record PostPaymentResponse
{
    public Guid Id { get; init; }
    public PaymentStatus Status { get; init; }
    public required string CardNumberLastFour { get; init; }
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public required string Currency { get; init; }
    public int Amount { get; init; }
}