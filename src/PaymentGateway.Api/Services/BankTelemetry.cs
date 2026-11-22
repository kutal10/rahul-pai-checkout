using System.Diagnostics;
using System.Diagnostics.Metrics;

using PaymentGateway.Api.Models.Enums;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Records duration metrics and writes logs for bank authorization attempts.
/// </summary>
public sealed class BankTelemetry(IMeterFactory meterFactory, ILogger<BankTelemetry> logger)
{
    public const string MeterName = "PaymentGateway.Bank";
    public const string AuthorizationDurationInstrumentName = "payment_gateway.bank.authorization.duration";
    public const string OutcomeTagName = "outcome";

    private const string MillisecondsUnit = "ms";

    private readonly Histogram<double> _duration = meterFactory.Create(MeterName)
        .CreateHistogram<double>(AuthorizationDurationInstrumentName, MillisecondsUnit,
            "Time spent requesting and reading a bank decision.");

    /// <summary>
    /// Records the attempt duration by outcome and logs a warning if the bank is unavailable or returns an invalid response.
    /// </summary>
    /// <param name="outcome">The result of the authorization attempt.</param>
    /// <param name="statusCode">The bank's HTTP status code, if available.</param>
    /// <param name="elapsed">The time taken by the authorization attempt.</param>
    internal void TrackAuthorizationOutcome(Outcome outcome, int? statusCode, TimeSpan elapsed)
    {
        var failed = outcome.Authorization is BankAuthorizationOutcome.Unavailable
            or BankAuthorizationOutcome.InvalidResponse;

        var durationMs = elapsed.TotalMilliseconds;
        var outcomeTag = new KeyValuePair<string, object?>(OutcomeTagName, outcome.Label);
        _duration.Record(durationMs, outcomeTag);
        var logLevel = failed ? LogLevel.Warning : LogLevel.Information;
        var traceId = Activity.Current?.TraceId.ToString();

        logger.Log(logLevel,
            "Bank authorization attempt ended with {BankOutcome} after {DurationMs} ms (HTTP {BankStatusCode}, trace {TraceId})",
            outcome.Label, durationMs, statusCode, traceId);
    }

    /// <summary>
    /// Pairs a specific attempt result, such as a timeout, with a label for logs and metrics
    /// and the <see cref="BankAuthorizationOutcome"/> returned by the bank client.
    /// </summary>
    internal sealed class Outcome
    {
        internal static Outcome Authorized { get; } = new("authorized", BankAuthorizationOutcome.Authorized);
        internal static Outcome Declined { get; } = new("declined", BankAuthorizationOutcome.Declined);
        internal static Outcome BankUnavailable { get; } = new("bank_unavailable", BankAuthorizationOutcome.Unavailable);
        internal static Outcome UnexpectedStatus { get; } = new("unexpected_status", BankAuthorizationOutcome.InvalidResponse);
        internal static Outcome InvalidPayload { get; } = new("invalid_payload", BankAuthorizationOutcome.InvalidResponse);
        internal static Outcome TransportError { get; } = new("transport_error", BankAuthorizationOutcome.Unavailable);
        internal static Outcome Timeout { get; } = new("timeout", BankAuthorizationOutcome.Unavailable);

        private Outcome(string label, BankAuthorizationOutcome authorization)
        {
            Label = label;
            Authorization = authorization;
        }

        internal string Label { get; }
        internal BankAuthorizationOutcome Authorization { get; }
    }
}