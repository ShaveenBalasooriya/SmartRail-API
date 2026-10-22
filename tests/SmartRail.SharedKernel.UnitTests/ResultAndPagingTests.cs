using SmartRail.SharedKernel.Paging;
using SmartRail.SharedKernel.Primitives;
using SmartRail.SharedKernel.Results;

namespace SmartRail.SharedKernel.UnitTests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_carries_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_carries_the_error_it_was_given()
    {
        var error = Error.Validation("departure.invalid", "The departure time must be in the past.");

        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Successful_generic_result_exposes_its_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws()
    {
        var result = Result.Failure<int>(Error.NotFound("journey.missing", "No such journey."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_success_cannot_carry_an_error()
    {
        Assert.Throws<InvalidOperationException>(() => new ProbeResult(isSuccess: true, Error.NotFound("x", "y")));
    }

    [Fact]
    public void A_failure_must_carry_an_error()
    {
        Assert.Throws<InvalidOperationException>(() => new ProbeResult(isSuccess: false, Error.None));
    }

    [Fact]
    public void Error_factories_set_the_matching_type()
    {
        Assert.Equal(ErrorType.NotFound, Error.NotFound("a", "b").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("a", "b").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("a", "b").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("a", "b").Type);
    }

    private sealed class ProbeResult(bool isSuccess, Error error) : Result(isSuccess, error);
}

public sealed class ErrorsAreComparedByValueTests
{
    [Fact]
    public void Two_errors_with_the_same_code_and_description_are_equal()
    {
        Assert.Equal(
            Error.Validation("code", "description"),
            Error.Validation("code", "description"));
    }
}

public sealed class ValueObjectTests
{
    [Fact]
    public void Two_value_objects_with_the_same_components_are_equal()
    {
        var first = new StationCode("CEN");
        var second = new StationCode("CEN");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        var first = new StationCode("CEN");
        var second = new StationCode("MRD");

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void Value_objects_of_different_types_are_never_equal()
    {
        var code = new StationCode("CEN");
        var other = new LineCode("CEN");

        Assert.NotEqual(code, (object)other);
        Assert.False(code == (ValueObject)other);
    }

    [Fact]
    public void A_value_object_is_not_equal_to_null()
    {
        var code = new StationCode("CEN");

        Assert.False(code.Equals(null));
        Assert.False(code == null);
        Assert.True(code != null);
    }

    [Fact]
    public void A_value_object_equals_itself()
    {
        var code = new StationCode("CEN");

        Assert.True(code.Equals(code));
    }

    private sealed class StationCode(string value) : ValueObject
    {
        public string Value { get; } = value;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Value;
        }
    }

    private sealed class LineCode(string value) : ValueObject
    {
        public string Value { get; } = value;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Value;
        }
    }
}

public sealed class EntityTests
{
    [Fact]
    public void Two_entities_of_the_same_type_with_the_same_id_are_equal()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(new ProbeEntity(id), new ProbeEntity(id));
    }

    [Fact]
    public void Entities_with_different_ids_are_not_equal()
    {
        Assert.NotEqual(new ProbeEntity(Guid.CreateVersion7()), new ProbeEntity(Guid.CreateVersion7()));
    }

    [Fact]
    public void An_entity_records_and_clears_its_domain_events()
    {
        var entity = new ProbeEntity(Guid.CreateVersion7());

        entity.RaiseEvent(new ProbeEvent(DateTimeOffset.UnixEpoch));

        Assert.Single(entity.DomainEvents);

        entity.ClearDomainEvents();

        Assert.Empty(entity.DomainEvents);
    }

    private sealed class ProbeEntity(Guid id) : Entity<Guid>(id)
    {
        public void RaiseEvent(IDomainEvent domainEvent) => Raise(domainEvent);
    }

    private sealed record ProbeEvent(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
}

public sealed class PagingTests
{
    [Fact]
    public void A_page_request_clamps_its_size_and_page()
    {
        Assert.Equal(1, new PageRequest(0, 0).Page);
        Assert.Equal(PageRequest.DefaultPageSize, new PageRequest(0, 0).PageSize);
        Assert.Equal(PageRequest.MaxPageSize, new PageRequest(1, 10_000).PageSize);
    }

    [Fact]
    public void A_page_request_computes_its_skip()
    {
        Assert.Equal(0, new PageRequest(1, 25).Skip);
        Assert.Equal(50, new PageRequest(3, 25).Skip);
    }

    [Fact]
    public void A_paged_result_maps_items_to_pages()
    {
        var page = new PagedResult<int>([1, 2, 3], page: 1, pageSize: 3, totalCount: 7);

        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.False(page.HasPreviousPage);
    }

    [Fact]
    public void The_last_page_has_no_next_page()
    {
        var page = new PagedResult<int>([7], page: 3, pageSize: 3, totalCount: 7);

        Assert.False(page.HasNextPage);
        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public void An_empty_result_has_no_pages()
    {
        var page = new PagedResult<int>([], page: 1, pageSize: 25, totalCount: 0);

        Assert.Equal(0, page.TotalPages);
        Assert.False(page.HasNextPage);
    }
}
