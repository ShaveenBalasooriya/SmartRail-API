using MediatR;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.Application.Messaging;

/// <summary>A use case that changes state and reports success or an expected failure.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>A use case that changes state and returns a value on success.</summary>
/// <typeparam name="TResponse">The success value.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;
