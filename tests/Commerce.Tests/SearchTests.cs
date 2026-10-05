using Commerce.Core.Search;

namespace Commerce.Tests;

public class SearchTests
{
    private readonly ProductSearch _search = new(TestCatalog.Load());

    private string[] Ids(string? query, decimal? maxPrice = null) =>
        _search.Search(new SearchRequest(query, maxPrice, null)).Matches.Select(m => m.Product.Id).ToArray();

    [Theory]
    [InlineData("I want to improve my Double Axel and I'm looking for an online program under $350.")]
    [InlineData("Does Victory Skating have an online Double Axel training program?")]
    [InlineData("Does VSA offer online training for Double Axel?")]
    [InlineData("2A")]
    [InlineData("2axel club")]
    public void Double_axel_requests_find_the_double_axel_club_alone(string query)
    {
        Assert.Equal(["vsa-double-axel-club"], Ids(query));
    }

    [Theory]
    [InlineData("single axel", "vsa-axel-club")]
    [InlineData("I'm working on my doubles", "vsa-double-jumps-club")]
    [InlineData("double lutz", "vsa-double-jumps-club")]
    [InlineData("triple salchow", "vsa-triple-jumps-club")]
    public void Other_skills_route_to_their_level(string query, string expected)
    {
        Assert.Equal(expected, Ids(query)[0]);
    }

    [Theory]
    [InlineData("What does VSA offer?")]
    [InlineData("Victory Skating programs")]
    [InlineData(null)]
    public void Brand_only_questions_list_the_whole_ladder_in_level_order(string? query)
    {
        Assert.Equal(["vsa-axel-club", "vsa-double-jumps-club", "vsa-double-axel-club", "vsa-triple-jumps-club"], Ids(query));
    }

    [Fact]
    public void Budget_is_compared_against_the_package_price()
    {
        var within = _search.Search(new SearchRequest("double axel", 350, "USD")).Matches.Single();
        var over = _search.Search(new SearchRequest("double axel", 200, "USD")).Matches.Single();

        Assert.True(within.WithinBudget);
        Assert.Contains("$299 for 6 months is within the USD 350 budget", within.Reasons);
        Assert.False(over.WithinBudget);
    }

    [Fact]
    public void A_budget_in_another_currency_is_not_guessed()
    {
        var match = _search.Search(new SearchRequest("double axel", 300, "EUR")).Matches.Single();

        Assert.Null(match.WithinBudget);
    }

    [Fact]
    public void An_unrelated_query_says_so_and_lists_everything()
    {
        var result = _search.Search(new SearchRequest("ice hockey goalie camp", null, null));

        Assert.True(result.QueryMatchedNothing);
        Assert.Equal(4, result.Matches.Count);
    }
}
