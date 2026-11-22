using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

internal sealed class PaymentRejectionProblem : HttpValidationProblemDetails
{
    public PaymentRejectionProblem(IDictionary<string, string[]> errors) : base(errors)
    {
        Status = StatusCodes.Status400BadRequest;
        Title = "The payment request was rejected.";
    }

    [Required]
    public PaymentRejectionStatus PaymentStatus => PaymentRejectionStatus.Rejected;
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentRejectionStatus>))]
internal enum PaymentRejectionStatus
{
    Rejected
}