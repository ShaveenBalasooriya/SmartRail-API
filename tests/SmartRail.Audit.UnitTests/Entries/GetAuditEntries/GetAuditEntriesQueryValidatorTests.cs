using SmartRail.Audit.Application.Entries.GetAuditEntries;
using SmartRail.Audit.UnitTests.TestDoubles;

namespace SmartRail.Audit.UnitTests.Entries.GetAuditEntries;

/// <summary>
/// The list's input rules (design D6, implementation-plan D-4): the outcome is succeeded or denied,
/// the range's start is not after its end, and a page holds at most 100 entries. Each error is keyed
/// by the field the caller sent.
/// </summary>
public sealed class GetAuditEntriesQueryValidatorTests
{
    private static readonly GetAuditEntriesQueryValidator Validator = new();

    [Fact]
    public void Happy_path_accepts_an_empty_filter_and_the_default_page()
    {
        // Arrange
        var query = new GetAuditEntriesQuery(
            ActorId: null,
            From: null,
            To: null,
            Outcome: null,
            Page: 1,
            PageSize: 25);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("succeeded")]
    [InlineData("SUCCEEDED")]
    [InlineData("denied")]
    [InlineData("Denied")]
    public void A_known_outcome_or_a_blank_one_is_accepted(string? outcome)
    {
        // Arrange
        var query = new GetAuditEntriesQuery(null, null, null, outcome, 1, 25);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.True(result.IsValid, $"'{outcome}' should be an acceptable outcome.");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("0")]
    [InlineData("1")]
    public void Any_other_outcome_is_rejected_on_the_outcome_field(string outcome)
    {
        // Arrange
        var query = new GetAuditEntriesQuery(null, null, null, outcome, 1, 25);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("outcome", error.PropertyName);
        Assert.Equal("The outcome must be succeeded or denied.", error.ErrorMessage);
    }

    [Fact]
    public void A_range_whose_start_is_after_its_end_is_rejected_on_the_from_field()
    {
        // Arrange
        var from = TestData.Now;
        var to = TestData.Now.AddHours(-1);
        var query = new GetAuditEntriesQuery(null, from, to, null, 1, 25);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("from", error.PropertyName);
        Assert.Equal("The start of the range must not be after its end.", error.ErrorMessage);
    }

    [Fact]
    public void A_range_whose_start_and_end_are_equal_is_accepted()
    {
        // Arrange
        var instant = TestData.Now;
        var query = new GetAuditEntriesQuery(null, instant, instant, null, 1, 25);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(99, true)]
    [InlineData(101, false)]
    [InlineData(200, false)]
    public void The_page_size_limit_is_one_hundred(int pageSize, bool expectedValid)
    {
        // Arrange
        var query = new GetAuditEntriesQuery(null, null, null, null, 1, pageSize);

        // Act
        var result = Validator.Validate(query);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal("pageSize", error.PropertyName);
            Assert.Equal("The page size must be 100 or less.", error.ErrorMessage);
        }
    }
}
