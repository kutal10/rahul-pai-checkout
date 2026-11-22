using System.Diagnostics;
using System.Net;
using System.Text.Json;

using PaymentGateway.Api.Models.Bank;
using PaymentGateway.Api.Models.Enums;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public sealed class BankClient(HttpClient httpClient, BankTelemetry telemetry)
{
    private const string PaymentPath = "payments";

    public async Task<BankAuthorizationOutcome> AuthorizeAsync(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var bankRequest = new BankPaymentRequest(
            request.CardNumber,
            FormattableString.Invariant($"{request.ExpiryMonth:00}/{request.ExpiryYear:0000}"),
            request.Currency,
            request.Amount!.Value,
            request.Cvv);

        var stopwatch = Stopwatch.StartNew();
        HttpStatusCode? statusCode = null;
        try
        {
            using var response = await httpClient.PostAsJsonAsync(PaymentPath, bankRequest, cancellationToken);
            statusCode = response.StatusCode;

            if (statusCode == HttpStatusCode.ServiceUnavailable)
                return TrackAndReturnOutcome(BankTelemetry.Outcome.BankUnavailable);

            if (!response.IsSuccessStatusCode)
                return TrackAndReturnOutcome(BankTelemetry.Outcome.UnexpectedStatus);

            BankPaymentResponse? payment;
            try
            {
                payment = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                // An unsupported response charset causes ReadFromJsonAsync to throw InvalidOperationException.
                return TrackAndReturnOutcome(BankTelemetry.Outcome.InvalidPayload);
            }

            return payment?.Authorized switch
            {
                true => TrackAndReturnOutcome(BankTelemetry.Outcome.Authorized),
                false => TrackAndReturnOutcome(BankTelemetry.Outcome.Declined),
                null => TrackAndReturnOutcome(BankTelemetry.Outcome.InvalidPayload)
            };
        }
        catch (HttpRequestException exception)
        {
            statusCode ??= exception.StatusCode;
            return exception.StatusCode switch
            {
                null => TrackAndReturnOutcome(BankTelemetry.Outcome.TransportError),
                HttpStatusCode.ServiceUnavailable => TrackAndReturnOutcome(BankTelemetry.Outcome.BankUnavailable),
                _ => TrackAndReturnOutcome(BankTelemetry.Outcome.UnexpectedStatus)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TrackAndReturnOutcome(BankTelemetry.Outcome.Timeout);
        }

        BankAuthorizationOutcome TrackAndReturnOutcome(BankTelemetry.Outcome outcome)
        {
            telemetry.TrackAuthorizationOutcome(outcome, (int?)statusCode, stopwatch.Elapsed);
            return outcome.Authorization;
        }
    }
}