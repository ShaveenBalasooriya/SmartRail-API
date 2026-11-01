using MediatR;
using SmartRail.IdentityAccess.Application.Staff.GetStaffDisplayNames;
using SmartRail.SharedKernel.Results;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>Records the staff directory query and returns a configured successful result.</summary>
internal sealed class SpySender(IReadOnlyDictionary<Guid, string> response) : ISender
{
    public List<GetStaffDisplayNamesQuery> Queries { get; } = [];

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        Queries.Add(Assert.IsType<GetStaffDisplayNamesQuery>(request));
        return Task.FromResult((TResponse)(object)Result.Success(response));
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The staff directory sends one typed query.");

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest =>
        throw new NotSupportedException("The staff directory sends one typed query.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The staff directory does not stream requests.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The staff directory does not stream requests.");
}
