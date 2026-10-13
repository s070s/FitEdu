using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Blog tags are stored as English keywords; the chips show HomeRes labels, so Greek pages never
/// show an English tag for a seeded article.
/// </summary>
public class BlogTagLabelsTests
{
    private const char FirstGreekLetter = 'Ά';
    private const char LastGreekLetter = 'ώ';

    [Fact]
    public void Greek_EverySeededTag_HasAGreekLabel()
    {
        using var cultureScope = new CultureScope("el");

        var seededTags = BlogSeedData.SeedArticles.SelectMany(article => article.Tags ?? []).Distinct();

        seededTags.Select(BlogTagLabels.Label).ShouldAllBe(label => label.Any(IsGreekLetter));
    }

    [Theory]
    [InlineData("en", "Strength", "Strength")]
    [InlineData("el", "Recovery", "Αποκατάσταση")]
    [InlineData("el", " Beginners ", "Αρχάριοι")]
    public void Label_ReturnsTheLabelOfTheCurrentLanguage(string culture, string tag, string expected)
    {
        using var cultureScope = new CultureScope(culture);

        BlogTagLabels.Label(tag).ShouldBe(expected);
    }

    [Fact]
    public void Label_UnknownTag_IsShownAsItIs()
    {
        using var cultureScope = new CultureScope("el");

        BlogTagLabels.Label("Kettlebells").ShouldBe("Kettlebells");
    }

    private static bool IsGreekLetter(char character) =>
        character >= FirstGreekLetter && character <= LastGreekLetter;
}
