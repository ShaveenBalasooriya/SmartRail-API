using MediatR;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.SharedKernel.Results;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

/// <summary>
/// Records the fact-application commands sent by a handler. No other MediatR request is expected.
/// A result failure or exception can be supplied for dependency-failure tests.
/// </summary>
internal sealed class SpySender : ISender
{
    public List<ApplyJourneyFactCommand> Commands { get; } = [];

    public Error? FailureToReturn { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public Guid? FailingJourneyId { get; set; }

    public Action<ApplyJourneyFactCommand>? OnSend { get; set; }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        var command = Assert.IsType<ApplyJourneyFactCommand>(request);
        Commands.Add(command);
        OnSend?.Invoke(command);

        if (ExceptionToThrow is not null || command.JourneyId == FailingJourneyId)
        {
            throw ExceptionToThrow ?? new InvalidOperationException("Simulated fact-sender failure.");
        }

        var result = FailureToReturn is null
            ? Result.Success()
            : Result.Failure(FailureToReturn);

        return Task.FromResult((TResponse)(object)result);
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The handler sends one typed fact command.");

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest =>
        throw new NotSupportedException("The handler sends one typed fact command.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The handler sends one typed fact command.");

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The handler sends one typed fact command.");
}
