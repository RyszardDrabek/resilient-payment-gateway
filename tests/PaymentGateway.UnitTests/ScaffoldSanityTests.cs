using FluentAssertions;
using PaymentGateway.Domain;

namespace PaymentGateway.UnitTests;

public sealed class ScaffoldSanityTests
{
    [Fact]
    public void Domain_assembly_marker_is_loadable()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.Should().Be("PaymentGateway.Domain");
    }
}
