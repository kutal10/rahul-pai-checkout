using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest : IValidatableObject
{
    [Required]
    [RegularExpression("^[0-9]{14,19}$")]
    public string CardNumber { get; init; } = null!;

    [Required]
    [Range(1, 12)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? ExpiryMonth { get; init; }

    [Required]
    [Range(1, 9999)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? ExpiryYear { get; init; }

    [Required]
    [RegularExpression("^(GBP|USD|EUR)$", ErrorMessage = "Currency must be GBP, USD, or EUR.")]
    public string Currency { get; init; } = null!;

    [Required]
    [Range(1, int.MaxValue)]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? Amount { get; init; }

    [Required]
    [RegularExpression("^[0-9]{3,4}$")]
    public string Cvv { get; init; } = null!;

    /// <summary>
    /// Checks the expiry month and year together after the individual request fields pass validation.
    /// The card is valid through the end of its expiry month in UTC.
    /// </summary>
    /// <param name="validationContext">Supplies the clock used to check the expiry date.</param>
    /// <returns>An expiry error for both fields if the card has expired.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var clock = validationContext.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = clock.GetUtcNow();
        var hasExpired = ExpiryYear < now.Year
            || (ExpiryYear == now.Year && ExpiryMonth < now.Month);

        if (hasExpired)
        {
            yield return new ValidationResult(
                "The card has expired.",
                [nameof(ExpiryMonth), nameof(ExpiryYear)]);
        }
    }
}