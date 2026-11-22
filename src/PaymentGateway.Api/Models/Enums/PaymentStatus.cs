using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Authorized,
    Declined
}