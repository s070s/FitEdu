using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the testimonial editor's Greek role and quote: they load from the testimonial
/// and are sent on save (blank Greek text is sent as null, so the English text shows instead).
/// </summary>
public class AdminTestimonialEditorTests : BunitContext
{
    private readonly IAdminTestimonialConsumer _testimonialConsumer = Substitute.For<IAdminTestimonialConsumer>();
    private SaveTestimonialRequest? _savedRequest;

    public AdminTestimonialEditorTests()
    {
        Services.AddSingleton(_testimonialConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The unsaved-changes guard calls JS; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _testimonialConsumer
            .CreateAsync(Arg.Do<SaveTestimonialRequest>(request => _savedRequest = request), Arg.Any<CancellationToken>())
            .Returns(call => Testimonial(Guid.NewGuid()));
        _testimonialConsumer
            .UpdateAsync(Arg.Any<Guid>(), Arg.Do<SaveTestimonialRequest>(request => _savedRequest = request), Arg.Any<CancellationToken>())
            .Returns(call => Testimonial(call.ArgAt<Guid>(0)));
    }

    private static AdminTestimonialDto Testimonial(Guid id) => new(
        id, "Eleni Papadopoulou", "Ran her first 5K", null, "I ran 5K without stopping.", null, 1, true,
        DateTimeOffset.UtcNow, null,
        RoleOrTitleEl: "Έτρεξε τα πρώτα της 5 χλμ", QuoteTextEl: "Έτρεξα 5 χλμ χωρίς στάση.");

    private static void ClickSave(IRenderedComponent<AdminTestimonialEditor> page) =>
        page.FindAll("button").First(button => button.TextContent.Trim() == AdminRes.Save).Click();

    [Fact]
    public void ExistingTestimonial_LoadsTheGreekRoleAndQuoteIntoTheirInputs()
    {
        var id = Guid.NewGuid();
        _testimonialConsumer.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Testimonial(id));

        var page = Render<AdminTestimonialEditor>(parameters => parameters.Add(p => p.Id, id));

        page.Find("#t-role-el").GetAttribute("value").ShouldBe("Έτρεξε τα πρώτα της 5 χλμ");
        page.Find("#t-quote-el").GetAttribute("value").ShouldBe("Έτρεξα 5 χλμ χωρίς στάση.");
        page.Find("label[for=t-role-el]").TextContent.ShouldContain(AdminRes.FieldRoleTitleEl);
        page.Find("label[for=t-quote-el]").TextContent.ShouldContain(AdminRes.FieldQuoteEl);
    }

    [Fact]
    public void Save_SendsTheGreekRoleAndQuote()
    {
        var page = Render<AdminTestimonialEditor>();

        page.Find("#t-fullname").Change("Marcus Bennett");
        page.Find("#t-quote").Change("Ten minutes a day helped.");
        page.Find("#t-role-el").Change(" Εργαζόμενος γραφείου, 42 ");
        page.Find("#t-quote-el").Change("Δέκα λεπτά τη μέρα βοήθησαν.");
        ClickSave(page);

        _savedRequest.ShouldNotBeNull();
        _savedRequest.RoleOrTitleEl.ShouldBe("Εργαζόμενος γραφείου, 42");
        _savedRequest.QuoteTextEl.ShouldBe("Δέκα λεπτά τη μέρα βοήθησαν.");
    }

    [Fact]
    public void Save_BlankGreekFields_SendsNull()
    {
        var page = Render<AdminTestimonialEditor>();

        page.Find("#t-fullname").Change("Marcus Bennett");
        page.Find("#t-quote").Change("Ten minutes a day helped.");
        page.Find("#t-role-el").Change("   ");
        ClickSave(page);

        _savedRequest.ShouldNotBeNull();
        _savedRequest.RoleOrTitleEl.ShouldBeNull();
        _savedRequest.QuoteTextEl.ShouldBeNull();
    }
}
