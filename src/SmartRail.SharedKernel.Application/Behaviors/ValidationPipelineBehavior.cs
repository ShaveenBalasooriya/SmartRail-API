using FluentValidation;
using MediatR;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.Application.Behaviors;

/// <summary>
/// Runs every <see cref="IValidator{T}"/> for the request before its handler. A failure returns a
/// <see cref="ValidationResult"/> (or <see cref="ValidationResult{TValue}"/>) carrying every error,
/// so the handler never runs and nothing throws (design D1).
/// </summary>
/// <typeparam name="TRequest">The request being handled.</typeparam>
/// <typeparam name="TResponse">The request's response.</typeparam>
public sealed class ValidationPipelineBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
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

        var context = new ValidationContext<TRequest>(request);
        var errors = new List<Error>();

        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(context, cancellationToken);

            foreach (var failure in validation.Errors)
            {
                // The error code is the request property, so ToProblemDetails can key the
                // response's errors object by field name (api-conventions spec).
                errors.Add(Error.Validation(
                    string.IsNullOrWhiteSpace(failure.PropertyName) ? "request" : failure.PropertyName,
                    failure.ErrorMessage));
            }
        }

        if (errors.Count == 0)
        {
            return await next(cancellationToken);
        }

        return (TResponse)(object)ValidationResult.For<TResponse>(errors);
    }
}
