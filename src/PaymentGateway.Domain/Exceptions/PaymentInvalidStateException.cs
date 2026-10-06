using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Exceptions;

public sealed class PaymentInvalidStateException(PaymentState currentState, string requestedTransition)
    : Exception($"Cannot perform transition '{requestedTransition}' on payment currently in '{currentState}' state.")
{
    public PaymentState CurrentState { get; } = currentState;
    public string RequestedTransition { get; } = requestedTransition;
}
