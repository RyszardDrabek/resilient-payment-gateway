namespace PaymentGateway.Domain.Entities;

public enum DiscrepancyType
{
    None,
    AmountMismatch,
    MissingInGateway,
    MissingInAcquirer
}
