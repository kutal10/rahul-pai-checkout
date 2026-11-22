using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Models.Enums;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public partial class PaymentEndpointsTests
{
    [Fact]
    public async Task PostPayment_ValidPayment_SendsAuthorizationRequestToBank()
    {
        // arrange
        _bank.RespondWithDecision(authorized: true);
        var expectedBody = new JsonObject
        {
            ["card_number"] = TestCardNumber,
            ["expiry_date"] = "07/2030",
            [CurrencyField] = TestCurrency,
            [AmountField] = TestAmount,
            [CvvField] = TestCvv
        };

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);

        // assert
        var request = Assert.Single(_bank.Requests);
        Assert.Equal((HttpMethod.Post, "/payments"), (request.Method, request.Path));
        Assert.NotNull(request.Body);
        Assert.Equal(expectedBody, JsonNode.Parse(request.Body), JsonNode.DeepEquals);
    }

    [Theory]
    [InlineData(true, nameof(PaymentStatus.Authorized))]
    [InlineData(false, nameof(PaymentStatus.Declined))]
    public async Task PostPayment_BankReturnsDecision_CreatesPaymentWithDecision(bool authorized, string expectedStatus)
    {
        // arrange
        _bank.RespondWithDecision(authorized);

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);
        var payment = await response.Content.ReadFromJsonAsync<JsonElement>();

        // assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedStatus, payment.GetProperty(StatusField).GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostPayment_BankReturnsDecision_ReturnsMaskedPaymentDetails(bool authorized)
    {
        // arrange
        _bank.RespondWithDecision(authorized);
        var expectedDetails = new JsonObject
        {
            ["cardNumberLastFour"] = "0007",
            [ExpiryMonthField] = TestExpiryMonth,
            [ExpiryYearField] = TestExpiryYear,
            [CurrencyField] = TestCurrency,
            [AmountField] = TestAmount
        };

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);
        var payment = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        // assert
        payment.Remove(IdField);
        payment.Remove(StatusField);
        Assert.Equal(expectedDetails, payment, JsonNode.DeepEquals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostPayment_PaymentIsCreated_ReturnsLocationForGeneratedId(bool authorized)
    {
        // arrange
        _bank.RespondWithDecision(authorized);

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);
        var payment = await response.Content.ReadFromJsonAsync<JsonElement>();

        // assert
        var id = payment.GetProperty(IdField).GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal($"{PaymentsPath}/{id}", response.Headers.Location?.AbsolutePath, ignoreCase: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetPayment_PaymentExists_ReturnsStoredPayment(bool authorized)
    {
        // arrange
        _bank.RespondWithDecision(authorized);
        using var created = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);
        var payment = await created.Content.ReadFromJsonAsync<JsonNode>();

        // act
        using var response = await _client.GetAsync(created.Headers.Location);
        var retrieved = await response.Content.ReadFromJsonAsync<JsonNode>();

        // assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payment, retrieved, JsonNode.DeepEquals);
        Assert.Single(_bank.Requests);
    }

    [Fact]
    public async Task GetPayment_PaymentDoesNotExist_ReturnsNotFound()
    {
        // arrange
        var paymentId = Guid.NewGuid();

        // act
        using var response = await _client.GetAsync($"{PaymentsPath}/{paymentId}");

        // assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_bank.Requests);
    }

    public static TheoryData<string, object?> InvalidPaymentFields => new()
    {
        { CardNumberField, null },
        { CardNumberField, "1234567890123" },
        { CardNumberField, "12345678901234567890" },
        { CardNumberField, "1234567890123A" },
        { CardNumberField, 12345678901234L },
        { ExpiryMonthField, null },
        { ExpiryMonthField, 0 },
        { ExpiryMonthField, 13 },
        { ExpiryMonthField, 6 },
        { ExpiryYearField, null },
        { ExpiryYearField, 2029 },
        { CurrencyField, null },
        { CurrencyField, "JPY" },
        { CurrencyField, TestCurrency + " " },
        { AmountField, null },
        { AmountField, 0 },
        { AmountField, -1 },
        { AmountField, 1.5m },
        { AmountField, "1050" },
        { CvvField, null },
        { CvvField, "12" },
        { CvvField, "12345" },
        { CvvField, "12X" },
        { CvvField, 123 },
    };

    [Theory]
    [MemberData(nameof(InvalidPaymentFields))]
    public async Task PostPayment_InvalidField_ReturnsRejectionProblem(string field, object? value)
    {
        // arrange
        var payment = PaymentWithInvalidField(field, value);

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, payment);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // assert
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.GetProperty(StatusField).GetInt32());
        Assert.Equal(RejectedStatus, problem.GetProperty(PaymentStatusField).GetString());
        Assert.NotEmpty(problem.GetProperty(ErrorsField).EnumerateObject());
        Assert.False(problem.TryGetProperty(IdField, out _));
    }

    [Theory]
    [MemberData(nameof(InvalidPaymentFields))]
    public async Task PostPayment_InvalidField_RejectsBeforeCallingBank(string field, object? value)
    {
        // arrange
        var payment = PaymentWithInvalidField(field, value);

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, payment);

        // assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_bank.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.OK, HttpStatusCode.BadGateway)]
    public async Task PostPayment_BankReturnsNoDecision_ReturnsExpectedErrorStatus(
        HttpStatusCode bankStatus, HttpStatusCode expectedStatus)
    {
        // arrange
        _bank.RespondWith(bankStatus, "{}");

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);

        // assert
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Single(_bank.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.OK)]
    public async Task PostPayment_BankReturnsNoDecision_OmitsPaymentIdentifiers(HttpStatusCode bankStatus)
    {
        // arrange
        _bank.RespondWith(bankStatus, "{}");

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // assert
        Assert.Null(response.Headers.Location);
        Assert.False(problem.TryGetProperty(IdField, out _));
    }

    [Theory]
    [MemberData(nameof(InvalidBankPayloads))]
    public async Task PostPayment_BankReturnsInvalidPayload_ReturnsBadGateway(string mediaType, string body)
    {
        // arrange
        _bank.RespondWith(HttpStatusCode.OK, body, mediaType);

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);

        // assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Single(_bank.Requests);
    }

    [Theory]
    [MemberData(nameof(InvalidBankPayloads))]
    public async Task PostPayment_BankReturnsInvalidPayload_RecordsInvalidPayloadOutcome(string mediaType, string body)
    {
        // arrange
        const string expectedOutcome = "invalid_payload";
        _bank.RespondWith(HttpStatusCode.OK, body, mediaType);
        var meterFactory = _application.Services.GetRequiredService<IMeterFactory>();
        var measurements = new List<(double Duration, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == BankTelemetry.MeterName &&
                    instrument.Name == BankTelemetry.AuthorizationDurationInstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((_, duration, tags, _) => measurements.Add((duration, tags.ToArray())));
        listener.Start();

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);

        // assert
        var measurement = Assert.Single(measurements);
        Assert.True(measurement.Duration >= 0);
        Assert.Equal(new KeyValuePair<string, object?>[] { new(BankTelemetry.OutcomeTagName, expectedOutcome) }, measurement.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPayment_BankConnectionFails_ReturnsServiceUnavailable(bool timesOut)
    {
        // arrange
        _bank.FailWith(timesOut
            ? new TaskCanceledException("The bank request timed out.")
            : new HttpRequestException("Connection refused."));

        // act
        using var response = await _client.PostAsJsonAsync(PaymentsPath, ValidPayment);

        // assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(_bank.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData(MalformedJson)]
    public async Task PostPayment_BodyIsEmptyOrMalformed_ReturnsRejectionProblem(string body)
    {
        // arrange
        using var content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json);

        // act
        using var response = await _client.PostAsync(PaymentsPath, content);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RejectedStatus, problem.GetProperty(PaymentStatusField).GetString());
        Assert.NotEmpty(problem.GetProperty(ErrorsField).EnumerateObject());
        Assert.Empty(_bank.Requests);
    }

    [Theory]
    [InlineData(MediaTypeNames.Text.Plain)]
    [InlineData(JsonWithUnsupportedCharset)]
    public async Task PostPayment_MediaTypeIsUnsupported_RejectsBeforeCallingBank(string mediaType)
    {
        // arrange
        using var content = new StringContent("{}");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);

        // act
        using var response = await _client.PostAsync(PaymentsPath, content);

        // assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(_bank.Requests);
    }

    [Theory]
    [InlineData(CardNumberField, "^[0-9]{14,19}$")]
    [InlineData(CurrencyField, "^(GBP|USD|EUR)$")]
    [InlineData(CvvField, "^[0-9]{3,4}$")]
    public async Task GetOpenApi_PaymentRequestSchema_DocumentsFieldPattern(string field, string expectedPattern)
    {
        // act
        var document = await _client.GetFromJsonAsync<JsonElement>(OpenApiDocumentPath);

        // assert
        var post = PostPaymentOperation(document);
        var requestSchema = ContentSchema(post.GetProperty("requestBody"), MediaTypeNames.Application.Json);
        var properties = Schemas(document).GetProperty(SchemaName(requestSchema)).GetProperty("properties");
        Assert.Equal(expectedPattern, properties.GetProperty(field).GetProperty("pattern").GetString());
    }

    [Fact]
    public async Task GetOpenApi_SuccessfulPaymentOperations_ShareResponseSchema()
    {
        // act
        var document = await _client.GetFromJsonAsync<JsonElement>(OpenApiDocumentPath);

        // assert
        var getResponses = document.GetProperty("paths").GetProperty(OpenApiPaymentByIdPath).GetProperty("get").GetProperty("responses");
        var created = ResponseSchema(PostResponses(document), HttpStatusCode.Created, MediaTypeNames.Application.Json);
        Assert.Equal(created, ResponseSchema(getResponses, HttpStatusCode.OK, MediaTypeNames.Application.Json));
    }

    [Fact]
    public async Task GetOpenApi_CreatedPaymentSchema_DocumentsBankDecisionStatuses()
    {
        // act
        var document = await _client.GetFromJsonAsync<JsonElement>(OpenApiDocumentPath);

        // assert
        var schemas = Schemas(document);
        var created = ResponseSchema(PostResponses(document), HttpStatusCode.Created, MediaTypeNames.Application.Json);
        var status = schemas.GetProperty(created).GetProperty("properties").GetProperty(StatusField);
        Assert.Equal(new[] { "Authorized", "Declined" }, EnumValues(schemas, status));
    }

    [Fact]
    public async Task GetOpenApi_PaymentRejectionSchema_RequiresRejectedStatus()
    {
        // act
        var document = await _client.GetFromJsonAsync<JsonElement>(OpenApiDocumentPath);

        // assert
        var schemas = Schemas(document);
        var rejected = schemas.GetProperty(ResponseSchema(PostResponses(document), HttpStatusCode.BadRequest, MediaTypeNames.Application.ProblemJson));
        Assert.Contains(rejected.GetProperty("required").EnumerateArray(), name => name.GetString() == PaymentStatusField);
        var status = rejected.GetProperty("properties").GetProperty(PaymentStatusField);
        Assert.Equal(new[] { RejectedStatus }, EnumValues(schemas, status));
    }

    private static JsonElement PostResponses(JsonElement document) =>
        PostPaymentOperation(document).GetProperty("responses");

    private static JsonElement PostPaymentOperation(JsonElement document) =>
        document.GetProperty("paths").GetProperty(OpenApiPaymentsPath).GetProperty("post");

    private static JsonElement Schemas(JsonElement document) =>
        document.GetProperty("components").GetProperty("schemas");

    private static string ResponseSchema(JsonElement responses, HttpStatusCode status, string contentType) =>
        SchemaName(ContentSchema(responses.GetProperty(((int)status).ToString(CultureInfo.InvariantCulture)), contentType));

    private static JsonElement ContentSchema(JsonElement message, string contentType) =>
        message.GetProperty("content").GetProperty(contentType).GetProperty("schema");

    private static string SchemaName(JsonElement reference) => reference.GetProperty("$ref").GetString()!.Split('/')[^1];

    private static IEnumerable<string?> EnumValues(JsonElement schemas, JsonElement reference) =>
        schemas.GetProperty(SchemaName(reference)).GetProperty("enum").EnumerateArray().Select(value => value.GetString());
}