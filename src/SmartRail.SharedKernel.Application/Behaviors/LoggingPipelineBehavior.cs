using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.Application.Behaviors;

/// <summary>
/// Logs every request's type name, outcome and duration. The request itself is never logged, so a
/// command that carries passenger data cannot leak it into the log (design D1).
/// </summary>
/// <typeparam name="TRequest">The request being handled.</typeparam>
/// <typeparam name="TResponse">The request's response.</typeparam>
public sealed class LoggingPipelineBehavior<TRequest, TResponse>(
    ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;
        var startedAt = Stopwatch.GetTimestamp();

        var response = await next(cancellationToken);

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        var level = LogLevel.Information;
        var outcome = "success";

        if (response is Result { IsFailure: true } failure)
        {
            level = LogLevel.Warning;
            outcome = failure.Error.Code;
        }

        logger.Log(
            level,
            "Handled {RequestName} in {ElapsedMs:0.###} ms with {Outcome}",
            requestName,
            elapsedMs,
            outcome);

        return response;
    }
}
