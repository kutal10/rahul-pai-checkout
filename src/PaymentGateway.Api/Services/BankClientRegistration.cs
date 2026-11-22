namespace PaymentGateway.Api.Services;

internal static class BankClientRegistration
{
    public static IHttpClientBuilder AddBankClient(this IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = GetBankBaseAddress(configuration);

        return services.AddHttpClient<BankClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }

    private static Uri GetBankBaseAddress(IConfiguration configuration)
    {
        const string key = "Bank:BaseUrl";
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.Query.Length != 0 ||
            uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException($"{key} must be an absolute HTTP or HTTPS URL without a query or fragment.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}