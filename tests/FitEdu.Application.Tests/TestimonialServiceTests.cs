using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Application.ApiServices;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class TestimonialServiceTests
{
    private static TestimonialService NewService(ApplicationDbContext db) =>
        new(db, NullLogger<TestimonialService>.Instance);

    private static Testimonial Item(string name, int order, bool active = true) =>
        new() { Id = Guid.NewGuid(), FullName = name, QuoteText = "Quote", DisplayOrder = order, IsActive = active };

    private static SaveTestimonialRequest Request(string name = "Name", int order = 0, bool active = true) =>
        new(name, null, null, "Quote", order, active);

    [Fact]
    public async Task GetActive_ReturnsActiveOnly_OrderedByDisplayOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Testimonials.AddRange(Item("B", 2), Item("A", 1), Item("Hidden", 0, active: false));
        await db.SaveChangesAsync();

        var active = await NewService(db).GetActiveAsync();

        active.Select(t => t.FullName).ShouldBe(new[] { "A", "B" });
    }

    private static Testimonial BilingualItem(string? roleOrTitleEl, string? quoteTextEl) =>
        new()
        {
            Id = Guid.NewGuid(), FullName = "Eleni", DisplayOrder = 1,
            RoleOrTitle = "Ran her first 5K", RoleOrTitleEl = roleOrTitleEl,
            QuoteText = "I ran without stopping.", QuoteTextEl = quoteTextEl
        };

    [Fact]
    public async Task GetActive_Greek_ReturnsGreekRoleAndQuote()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Testimonials.Add(BilingualItem("Έτρεξε τα πρώτα της 5 χλμ", "Έτρεξα χωρίς στάση."));
        await db.SaveChangesAsync();

        var testimonial = (await NewService(db).GetActiveAsync("el")).Single();

        testimonial.RoleOrTitle.ShouldBe("Έτρεξε τα πρώτα της 5 χλμ");
        testimonial.QuoteText.ShouldBe("Έτρεξα χωρίς στάση.");
    }

    [Fact]
    public async Task GetActive_GreekWithoutTranslation_FallsBackToEnglish()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Testimonials.Add(BilingualItem(roleOrTitleEl: null, quoteTextEl: null));
        await db.SaveChangesAsync();

        var testimonial = (await NewService(db).GetActiveAsync("el")).Single();

        testimonial.RoleOrTitle.ShouldBe("Ran her first 5K");
        testimonial.QuoteText.ShouldBe("I ran without stopping.");
    }

    [Fact]
    public async Task GetActive_English_ReturnsEnglishRoleAndQuote()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Testimonials.Add(BilingualItem("Έτρεξε τα πρώτα της 5 χλμ", "Έτρεξα χωρίς στάση."));
        await db.SaveChangesAsync();

        var testimonial = (await NewService(db).GetActiveAsync("en")).Single();

        testimonial.RoleOrTitle.ShouldBe("Ran her first 5K");
        testimonial.QuoteText.ShouldBe("I ran without stopping.");
    }

    [Fact]
    public async Task CreateAndUpdate_StoreTheGreekRoleAndQuote_AndGetByIdReturnsThem()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var svc = NewService(db);

        var created = await svc.CreateAsync(Request() with { RoleOrTitleEl = "Δρομέας", QuoteTextEl = "Πρώτο" });
        await svc.UpdateAsync(created.Id, Request() with { RoleOrTitleEl = "Μαραθωνοδρόμος", QuoteTextEl = "Δεύτερο" });

        created.RoleOrTitleEl.ShouldBe("Δρομέας");
        created.QuoteTextEl.ShouldBe("Πρώτο");
        var updated = await svc.GetByIdAsync(created.Id);
        updated!.RoleOrTitleEl.ShouldBe("Μαραθωνοδρόμος");
        updated.QuoteTextEl.ShouldBe("Δεύτερο");
    }

    [Fact]
    public async Task GetById_Missing_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).GetByIdAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task Create_EmptyTable_AssignsOrderOne()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).CreateAsync(Request(order: 0))).DisplayOrder.ShouldBe(1);
    }

    [Fact]
    public async Task Create_ZeroOrder_AppendsAtMaxPlusOne()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Testimonials.Add(Item("Existing", 5));
        await db.SaveChangesAsync();

        (await NewService(db).CreateAsync(Request(order: 0))).DisplayOrder.ShouldBe(6);
    }

    [Fact]
    public async Task Create_ExplicitOrder_IsHonored()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).CreateAsync(Request(order: 3))).DisplayOrder.ShouldBe(3);
    }

    [Fact]
    public async Task Update_Missing_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).UpdateAsync(Guid.NewGuid(), Request())).ShouldBeNull();
    }

    [Fact]
    public async Task ToggleActive_FlipsState()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var t = Item("T", 1, active: true);
        db.Testimonials.Add(t);
        await db.SaveChangesAsync();

        (await NewService(db).ToggleActiveAsync(t.Id))!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task MoveUp_SwapsWithPrevious()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var first = Item("First", 1);
        var second = Item("Second", 2);
        db.Testimonials.AddRange(first, second);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        (await svc.MoveUpAsync(second.Id)).ShouldBeTrue();

        (await svc.GetByIdAsync(second.Id))!.DisplayOrder.ShouldBe(1);
        (await svc.GetByIdAsync(first.Id))!.DisplayOrder.ShouldBe(2);
    }

    [Fact]
    public async Task MoveUp_AtTop_ReturnsFalse()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var first = Item("First", 1);
        db.Testimonials.Add(first);
        await db.SaveChangesAsync();

        (await NewService(db).MoveUpAsync(first.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task MoveDown_AtBottom_ReturnsFalse()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var only = Item("Only", 1);
        db.Testimonials.Add(only);
        await db.SaveChangesAsync();

        (await NewService(db).MoveDownAsync(only.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task SetAndRemoveAvatar_UpdatesPath()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var t = Item("T", 1);
        db.Testimonials.Add(t);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        (await svc.SetAvatarPathAsync(t.Id, "avatars/x.jpg"))!.AvatarPath.ShouldBe("avatars/x.jpg");
        (await svc.RemoveAvatarAsync(t.Id)).ShouldBeTrue();
        (await svc.GetByIdAsync(t.Id))!.AvatarPath.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_Missing_ReturnsFalse_AndExisting_Removes()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var t = Item("T", 1);
        db.Testimonials.Add(t);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        (await svc.DeleteAsync(Guid.NewGuid())).ShouldBeFalse();
        (await svc.DeleteAsync(t.Id)).ShouldBeTrue();
        (await db.Testimonials.CountAsync()).ShouldBe(0);
    }
}
