namespace SmartRail.SharedKernel.Results;

/// <summary>
/// A failed result that carries every validation error, not just the first one. It is what the
/// validation pipeline behaviour returns, so a handler never runs and nothing throws (design D1).
/// </summary>
public interface IValidationResult
{
    /// <summary>Every error the request failed with, in the order they were found.</summary>
    IReadOnlyList<Error> Errors { get; }
}

/// <summary>
/// The validation failure of a request that returns a plain <see cref="Result"/>.
/// </summary>
public sealed class ValidationResult : Result, IValidationResult
{
    /// <summary>Creates a validation failure carrying every error.</summary>
    /// <exception cref="ArgumentException">No errors were supplied.</exception>
    public ValidationResult(IReadOnlyList<Error> errors)
        : base(false, FirstError(errors)) => Errors = [.. errors];

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>
    /// Creates the validation failure whose type matches the response a pipeline behaviour must
    /// return: a <see cref="ValidationResult"/> for <see cref="Result"/>, or a
    /// <see cref="ValidationResult{TValue}"/> for <see cref="Result{TValue}"/>.
    /// </summary>
    /// <typeparam name="TResponse">The response type of the request being handled.</typeparam>
    /// <param name="errors">Every validation error.</param>
    public static Result For<TResponse>(IReadOnlyList<Error> errors)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return new ValidationResult(errors);
        }

        if (typeof(TResponse).IsGenericType
            && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = typeof(TResponse).GetGenericArguments()[0];
            var failureType = typeof(ValidationResult<>).MakeGenericType(valueType);

            return (Result)Activator.CreateInstance(failureType, [errors])!;
        }

        throw new InvalidOperationException(
            $"A validation failure cannot be returned as '{typeof(TResponse)}'. "
            + "A command or query must return Result or Result<T>.");
    }

    private static Error FirstError(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("A validation failure must carry at least one error.", nameof(errors));
        }

        return errors[0];
    }
}

/// <summary>
/// The validation failure of a request that returns a <see cref="Result{TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The success value the result would have carried.</typeparam>
public sealed class ValidationResult<TValue> : Result<TValue>, IValidationResult
{
    /// <summary>Creates a validation failure carrying every error.</summary>
    /// <exception cref="ArgumentException">No errors were supplied.</exception>
    public ValidationResult(IReadOnlyList<Error> errors)
        : base(default, false, FirstError(errors)) => Errors = [.. errors];

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; }

    private static Error FirstError(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("A validation failure must carry at least one error.", nameof(errors));
        }

        return errors[0];
    }
}
