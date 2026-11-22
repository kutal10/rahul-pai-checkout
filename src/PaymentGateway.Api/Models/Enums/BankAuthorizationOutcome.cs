namespace PaymentGateway.Api.Models.Enums;

public enum BankAuthorizationOutcome
{
    Unavailable,
    InvalidResponse,
    Authorized,
    Declined
}