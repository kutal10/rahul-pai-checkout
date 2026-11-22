using System.Collections.Concurrent;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Stores payment results in memory and retrieves them by payment ID.
/// </summary>
/// <remarks>
/// Realistically this would be a db table somewhere
/// </remarks>
public class PaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();

    public void Add(PostPaymentResponse payment)
    {
        _payments[payment.Id] = payment;
    }

    public PostPaymentResponse? Get(Guid id)
    {
        return _payments.GetValueOrDefault(id);
    }
}