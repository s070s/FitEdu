using System.Net;
using System.Net.Http.Json;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Web.Consumers;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class TestimonialsIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public TestimonialsIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetActive_Anonymous_Returns200()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/testimonials")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TestimonialConsumer_PassesTheLanguage_AndGetsTheRoleAndQuoteTheApiPicks()
    {
        // Real chain: database -> service -> api/testimonials?lang= -> the consumer Home uses.
        var fullName = "Bilingual Member " + Guid.NewGuid().ToString("N");
        await _factory.SeedAsync(async db =>
        {
            db.Testimonials.Add(new Testimonial
            {
                Id = Guid.NewGuid(), FullName = fullName, DisplayOrder = 99, IsActive = true,
                RoleOrTitle = "Runner", RoleOrTitleEl = "Δρομέας",
                QuoteText = "Great programme.", QuoteTextEl = "Υπέροχο πρόγραμμα.",
                AvatarPath = "testimonials/avatars/bilingual.jpg"
            });
            await db.SaveChangesAsync();
        });
        var consumer = new TestimonialConsumer(_factory.CreateClient(), ConsumerTests.TokenProvider());

        var greek = (await consumer.GetActiveAsync("el"))!.Single(t => t.FullName == fullName);
        var english = (await consumer.GetActiveAsync("en"))!.Single(t => t.FullName == fullName);

        greek.RoleOrTitle.ShouldBe("Δρομέας");
        greek.QuoteText.ShouldBe("Υπέροχο πρόγραμμα.");
        english.RoleOrTitle.ShouldBe("Runner");
        english.QuoteText.ShouldBe("Great programme.");
        // Uploaded avatars resolve to the site-relative media endpoint, same rule as blog covers.
        english.AvatarUrl.ShouldBe("/api/media/testimonials/avatars/bilingual.jpg");
    }

    [Fact]
    public async Task AdminTestimonials_Create_GreekFieldsOverColumnLength_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var request = new SaveTestimonialRequest("Jane Tester", "QA", null, "Great platform!", 0, true,
            RoleOrTitleEl: new string('α', 151), QuoteTextEl: new string('α', 1001));

        var response = await client.PostAsJsonAsync("/api/admin/testimonials", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminTestimonials_Student_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync("/api/admin/testimonials")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminTestimonials_Create_Then_GetById_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var request = new SaveTestimonialRequest("Jane Tester", "QA", "Contoso", "Great platform!", 0, true);

        var created = await client.PostAsJsonAsync("/api/admin/testimonials", request);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<AdminTestimonialDto>();
        dto!.FullName.ShouldBe("Jane Tester");

        var fetched = await client.GetAsync($"/api/admin/testimonials/{dto.Id}");
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminTestimonials_GetById_Unknown_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await client.GetAsync($"/api/admin/testimonials/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminTestimonials_Create_FullNameOverColumnLength_Returns400()
    {
        // DQ-3: FullName must stay within TestimonialConfiguration's HasMaxLength(150) — a
        // longer value used to pass this DTO check and then throw a SQL truncation error on
        // SaveChanges (500) instead of a clean 400.
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");
        var request = new SaveTestimonialRequest(new string('a', 151), "QA", "Contoso", "Great platform!", 0, true);

        var response = await client.PostAsJsonAsync("/api/admin/testimonials", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
