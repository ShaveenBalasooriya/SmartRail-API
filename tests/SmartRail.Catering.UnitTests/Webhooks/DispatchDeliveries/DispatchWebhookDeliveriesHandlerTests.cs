using SmartRail.Catering.Application.Webhooks.DispatchDeliveries;
using SmartRail.Catering.UnitTests.TestDoubles;

namespace SmartRail.Catering.UnitTests.Webhooks.DispatchDeliveries;

public sealed class DispatchWebhookDeliveriesHandlerTests
{
    [Fact]
    public async Task Happy_path_runs_one_pump_pass_and_returns_its_attempt_count()
    {
        // Arrange
        var pump = new SpyWebhookDeliveryPump(result: 3);
        var handler = HandlerDriver.CreateCommandHandler<DispatchWebhookDeliveriesCommand, int>(pump);

        // Act
        var result = await handler.Handle(new DispatchWebhookDeliveriesCommand(), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(1, pump.CallCount);
    }

    [Fact]
    public async Task A_pump_failure_is_propagated()
    {
        // Arrange
        var pump = new SpyWebhookDeliveryPump
        {
            ExceptionToThrow = new InvalidOperationException("Pump unavailable."),
        };
        var handler = HandlerDriver.CreateCommandHandler<DispatchWebhookDeliveriesCommand, int>(pump);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new DispatchWebhookDeliveriesCommand(), CancellationToken.None));

        // Assert
        Assert.Equal("Pump unavailable.", exception.Message);
        Assert.Equal(1, pump.CallCount);
    }
}
