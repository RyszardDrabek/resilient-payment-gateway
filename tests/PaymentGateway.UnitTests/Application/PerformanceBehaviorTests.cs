using FluentAssertions;
using MediatR;
using PaymentGateway.Application.Behaviors;

namespace PaymentGateway.UnitTests.Application;

public sealed class PerformanceBehaviorTests
{
    private record TestRequest : IRequest<string>;

    [Fact]
    public async Task Handle_WhenSuccess_ReturnsResponse()
    {
        // Arrange
        var behavior = new PerformanceBehavior<TestRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        // Act
        var result = await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_WhenThrows_PropagatesException()
    {
        // Arrange
        var behavior = new PerformanceBehavior<TestRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromException<string>(new InvalidOperationException("boom"));

        // Act
        var act = () => behavior.Handle(new TestRequest(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
