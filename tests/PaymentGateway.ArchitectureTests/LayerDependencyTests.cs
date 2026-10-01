using FluentAssertions;
using NetArchTest.Rules;
using PaymentGateway.Domain;

namespace PaymentGateway.ArchitectureTests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_does_not_reference_infrastructure_or_adapters()
    {
        var result = Types.InAssembly(typeof(AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "PaymentGateway.Infrastructure",
                "PaymentGateway.Edge",
                "PaymentGateway.Api",
                "MassTransit",
                "Microsoft.EntityFrameworkCore",
                "WireMock",
                "Nethereum",
                "Adyen")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("PaymentGateway.Infrastructure", "PaymentGateway.Api", "MassTransit", "WireMock")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Edge()
    {
        // F-EDGE-01: Edge is an API-layer concern; Application must not take a dependency on it.
        var result = Types.InAssembly(typeof(Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("PaymentGateway.Edge")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: string.Join(", ", result.FailingTypeNames ?? []));
    }
}
