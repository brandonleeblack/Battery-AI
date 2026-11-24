using Battery.Core;

namespace Battery.Tests;

public class HintMergeTests
{
    [Fact]
    public void No_source_has_reported_gives_no_hint() =>
        Assert.Null(HintMerge.For(new[] { (false, (string?)null), (false, (string?)null) }));

    [Fact]
    public void A_working_source_clears_the_hint_even_if_another_wants_sign_in() =>
        Assert.Null(HintMerge.For(new[] { (true, (string?)"Right-click › Sign in to Claude"), (true, (string?)null) }));

    [Fact]
    public void A_source_that_has_not_reported_yet_does_not_count_as_working() =>
        Assert.Equal("Sign in", HintMerge.For(new[] { (true, (string?)"Sign in"), (false, (string?)null) }));

    [Fact]
    public void When_every_reported_source_has_a_hint_the_first_one_wins() =>
        Assert.Equal("first", HintMerge.For(new[] { (true, (string?)"first"), (true, (string?)"second") }));

    [Fact]
    public void A_single_source_passes_its_hint_through()
    {
        Assert.Equal("only", HintMerge.For(new[] { (true, (string?)"only") }));
        Assert.Null(HintMerge.For(new[] { (true, (string?)null) }));
    }

    [Fact]
    public void Stale_reading_shows_the_advice_even_though_another_source_once_worked() =>
        // The status line gave a reading an hour ago; claude.ai needs a sign-in. The advice is worth showing.
        Assert.Equal("Sign in", HintMerge.For(new[] { (true, (string?)"Sign in"), (true, (string?)null) }, readingIsFresh: false));

    [Fact]
    public void Fresh_reading_still_hides_the_advice() =>
        Assert.Null(HintMerge.For(new[] { (true, (string?)"Sign in"), (true, (string?)null) }, readingIsFresh: true));

    [Fact]
    public void Stale_reading_with_no_advice_anywhere_gives_no_hint() =>
        Assert.Null(HintMerge.For(new[] { (true, (string?)null), (true, (string?)null) }, readingIsFresh: false));
}
