using System.Net.Mime;

using Microsoft.AspNetCore.Http.HttpResults;

using PaymentGateway.Api.Models.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Endpoints;

internal static class PaymentEndpoints
{
    internal const string Route = "/api/Payments";

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        var payments = routes.MapGroup(Route).WithTags("Payments");

        payments.MapGet("/{id:guid}", GetPayment)
            .WithName(nameof(GetPayment))
            .ProducesProblem(StatusCodes.Status404NotFound);

        payments.MapPost(string.Empty, PostPayment)
            .WithName(nameof(PostPayment))
            .AddEndpointFilter(PaymentRequestValidation.ValidateAsync)
            .Produces<PaymentRejectionProblem>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static Results<Ok<PostPaymentResponse>, ProblemHttpResult> GetPayment(
        Guid id, PaymentsRepository paymentsRepository)
    {
        var payment = paymentsRepository.Get(id);
        return payment is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(payment);
    }

    private static async Task<Results<CreatedAtRoute<PostPaymentResponse>, ProblemHttpResult>> PostPayment(
        PostPaymentRequest paymentRequest,
        PaymentsRepository paymentsRepository,
        BankClient bankClient,
        CancellationToken cancellationToken)
    {
        var outcome = await bankClient.AuthorizeAsync(paymentRequest, cancellationToken);
        PaymentStatus status;
        switch (outcome)
        {
            case BankAuthorizationOutcome.Authorized:
                status = PaymentStatus.Authorized;
                break;
            case BankAuthorizationOutcome.Declined:
                status = PaymentStatus.Declined;
                break;
            case BankAuthorizationOutcome.Unavailable:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "The acquiring bank is unavailable.");
            default:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "The acquiring bank returned an unexpected response.");
        }

        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = status,
            CardNumberLastFour = paymentRequest.CardNumber[^4..],
            ExpiryMonth = paymentRequest.ExpiryMonth!.Value,
            ExpiryYear = paymentRequest.ExpiryYear!.Value,
            Currency = paymentRequest.Currency,
            Amount = paymentRequest.Amount!.Value
        };

        paymentsRepository.Add(payment);
        return TypedResults.CreatedAtRoute(payment, nameof(GetPayment), new { id = payment.Id });
    }
}