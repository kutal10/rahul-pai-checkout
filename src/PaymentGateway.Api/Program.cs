using PaymentGateway.Api.Endpoints;
using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<PaymentsRepository>();
builder.Services.AddSingleton<BankTelemetry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddBankClient(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UsePaymentRequestErrors();
app.MapPaymentEndpoints();
app.Run();

public partial class Program { }