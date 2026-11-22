using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Endpoints;

internal static class PaymentRequestValidation
{
    public static void UsePaymentRequestErrors(this WebApplication app)
    {
        app.UseWhen(IsPaymentPost, ConfigurePaymentRequestErrors);
    }

    private static bool IsPaymentPost(HttpContext context)
    {
        var request = context.Request;
        return HttpMethods.IsPost(request.Method) &&
            string.Equals(request.Path.Value?.TrimEnd('/'), PaymentEndpoints.Route, StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigurePaymentRequestErrors(IApplicationBuilder branch)
    {
        branch.Use(RejectUnknownJsonCharsetAsync);
        branch.UseStatusCodePages(WritePaymentRequestErrorAsync);
    }

    private static Task RejectUnknownJsonCharsetAsync(HttpContext context, RequestDelegate next)
    {
        // The .NET 8 JSON binder throws for unknown charsets before endpoint filters can run.
        var request = context.Request;
        if (request.HasJsonContentType() &&
            request.GetTypedHeaders().ContentType is { Charset.HasValue: true, Encoding: null })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType).ExecuteAsync(context);
        }

        return next(context);
    }

    private static Task WritePaymentRequestErrorAsync(StatusCodeContext statusCodeContext)
    {
        var context = statusCodeContext.HttpContext;
        switch (context.Response.StatusCode)
        {
            case StatusCodes.Status400BadRequest:
                {
                    var errors = new Dictionary<string, string[]>
                    {
                        [string.Empty] = ["A valid JSON payment request body is required."]
                    };
                    var problem = Rejected(errors);
                    return problem.ExecuteAsync(context);
                }

            case StatusCodes.Status415UnsupportedMediaType:
                {
                    var problem = TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);
                    return problem.ExecuteAsync(context);
                }

            default:
                return Task.CompletedTask;
        }
    }

    public static ValueTask<object?> ValidateAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Let ASP.NET Core finish handling binding failures before accessing the request argument.
        if (context.HttpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
            return next(context);

        var request = context.GetArgument<PostPaymentRequest>(0);
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request, context.HttpContext.RequestServices, items: null);

        if (Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
            return next(context);


        // Use an empty field name for errors that apply to the whole request.
        var errorsByField = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => (Member: member, Message: result.ErrorMessage!)))
            .GroupBy(error => error.Member, error => error.Message)
            .ToDictionary(field => field.Key, field => field.ToArray());

        return ValueTask.FromResult<object?>(Rejected(errorsByField));
    }

    private static ProblemHttpResult Rejected(IDictionary<string, string[]> errors)
    {
        return TypedResults.Problem(new PaymentRejectionProblem(errors));
    }
}