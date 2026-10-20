using MediatR;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.Application.Messaging;

/// <summary>A use case that reads state and returns a value on success.</summary>
/// <typeparam name="TResponse">The read value.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
