using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public partial class PaymentEndpointsTests : IDisposable
{
    private const string PaymentsPath = "/api/payments";
    private const string OpenApiDocumentPath = "/swagger/v1/swagger.json";
    private const string OpenApiPaymentsPath = "/api/Payments";
    private const string OpenApiPaymentByIdPath = OpenApiPaymentsPath + "/{id}";

    private const string CardNumberField = "cardNumber";
    private const string ExpiryMonthField = "expiryMonth";
    private const string ExpiryYearField = "expiryYear";
    private const string CurrencyField = "currency";
    private const string AmountField = "amount";
    private const string CvvField = "cvv";
    private const string IdField = "id";
    private const string StatusField = "status";
    private const string PaymentStatusField = "paymentStatus";
    private const string ErrorsField = "errors";
    private const string RejectedStatus = "Rejected";

    private const string TestCardNumber = "2222405343240007";
    private const int TestExpiryMonth = 7;
    private const int TestExpiryYear = 2030;
    private const string TestCurrency = "GBP";
    private const int TestAmount = 1050;
    private const string TestCvv = "012";

    private const string MalformedJson = "{";
    private const string JsonWithUnsupportedCharset = MediaTypeNames.Application.Json + "; charset=invalid";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly StubBank _bank = new();
    private readonly PaymentApplication _application;
    private readonly HttpClient _client;

    public PaymentEndpointsTests()
    {
        _application = new PaymentApplication(_bank);
        _client = _application.CreateClient();
    }

    public void Dispose()
    {
        _application.Dispose();
        _bank.Dispose();
    }

    public static TheoryData<string, string> InvalidBankPayloads => new()
    {
        { MediaTypeNames.Application.Json, MalformedJson },
        { JsonWithUnsupportedCharset, """{"authorized":true}""" }
    };

    private static PostPaymentRequest ValidPayment { get; } = new()
    {
        CardNumber = TestCardNumber,
        ExpiryMonth = TestExpiryMonth,
        ExpiryYear = TestExpiryYear,
        Currency = TestCurrency,
        Amount = TestAmount,
        Cvv = TestCvv
    };

    private static JsonObject PaymentWithInvalidField(string field, object? value)
    {
        var payment = (JsonObject)JsonSerializer.SerializeToNode(ValidPayment, WebJson)!;
        if (value is null)
        {
            payment.Remove(field);
        }
        else
        {
            payment[field] = JsonSerializer.SerializeToNode(value);
        }

        return payment;
    }

    private sealed class PaymentApplication(StubBank bank) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider, FixedClock>();
                services.AddHttpClient<BankClient>().ConfigurePrimaryHttpMessageHandler(() => bank);
            });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2030, 7, 31, 23, 59, 59, TimeSpan.Zero);
    }

    private sealed class StubBank : HttpMessageHandler
    {
        private readonly ConcurrentQueue<BankRequest> _requests = new();
        private (HttpStatusCode Status, string Body, string ContentType)? _response;
        private Exception? _failure;

        public IReadOnlyCollection<BankRequest> Requests => _requests;

        public void RespondWithDecision(bool authorized) =>
            RespondWith(HttpStatusCode.OK, JsonSerializer.Serialize(new { authorized, authorization_code = "bank-code" }));

        public void RespondWith(HttpStatusCode status, string body, string contentType = MediaTypeNames.Application.Json + "; charset=utf-8")
        {
            _response = (status, body, contentType);
            _failure = null;
        }

        public void FailWith(Exception failure)
        {
            _failure = failure;
            _response = null;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Enqueue(new BankRequest(request.Method, request.RequestUri!.AbsolutePath, body));

            if (_failure is not null)
            {
                throw _failure;
            }

            var response = _response ?? throw new InvalidOperationException("No response was configured for the acquiring bank.");
            var content = new StringContent(response.Body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(response.ContentType);
            return new HttpResponseMessage(response.Status) { Content = content };
        }
    }

    private sealed record BankRequest(HttpMethod Method, string Path, string? Body);
}