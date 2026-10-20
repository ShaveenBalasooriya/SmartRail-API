using MediatR;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.Application.Messaging;

/// <summary>Handles a query that returns a value on success.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The read value.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
