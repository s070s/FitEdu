using FitEdu.Application.ApiServices;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class CategoryServiceTests
{
    private static CategoryService NewService(ApplicationDbContext db) => new(db);

    [Fact]
    public async Task GetCategories_CoursesScope_OnlyCountsPublishedCourses()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Strength" };
        db.Categories.Add(category);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = category.Id });
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C2", IsPublished = false, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "en");

        var dto = result.ShouldHaveSingleItem();
        dto.Name.ShouldBe("Strength");
        dto.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetCategories_AssessmentsScope_CountsOnlyAssessments()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Strength" };
        db.Categories.Add(category);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = category.Id });
        db.AssessmentDefinitions.Add(new AssessmentDefinition
        {
            Id = Guid.NewGuid(), Key = "k1", TitleEn = "A1", SchemaJson = "{}", IsPublished = true, CategoryId = category.Id
        });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("assessments", "en");

        var dto = result.ShouldHaveSingleItem();
        dto.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetCategories_CoursesScope_LeavesOutAssessmentOnlyCategories()
    {
        // The catalogue chips must not offer Readiness, Safety, Mindset or Setup: only
        // self-assessments use them, so picking one would always show an empty list.
        await using var db = DbContextFactory.CreateInMemory();
        var strength = new Category { Id = Guid.NewGuid(), NameEn = "Strength" };
        var readiness = new Category { Id = Guid.NewGuid(), NameEn = "Readiness" };
        db.Categories.AddRange(strength, readiness);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = strength.Id });
        db.AssessmentDefinitions.Add(new AssessmentDefinition
        {
            Id = Guid.NewGuid(), Key = "k1", TitleEn = "A1", SchemaJson = "{}", IsPublished = true, CategoryId = readiness.Id
        });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "en");

        result.ShouldHaveSingleItem().Name.ShouldBe("Strength");
    }

    [Fact]
    public async Task GetCategories_CategoryWithNoPublishedItems_IsExcluded()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Empty" };
        db.Categories.Add(category);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = false, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "en");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCategories_UncategorizedPublishedItems_DoNotProduceEntry()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = null });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "en");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCategories_GreekFallsBackToEnglishWhenNameElNull()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Strength", NameEl = null };
        db.Categories.Add(category);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "el");

        result.ShouldHaveSingleItem().Name.ShouldBe("Strength");
    }

    [Fact]
    public async Task GetCategories_GreekUsesNameElWhenPresent()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Strength", NameEl = "Δύναμη" };
        db.Categories.Add(category);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "el");

        result.ShouldHaveSingleItem().Name.ShouldBe("Δύναμη");
    }

    [Fact]
    public async Task GetCategories_OrderedByName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var zeta = new Category { Id = Guid.NewGuid(), NameEn = "Zeta" };
        var alpha = new Category { Id = Guid.NewGuid(), NameEn = "Alpha" };
        db.Categories.AddRange(zeta, alpha);
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C1", IsPublished = true, CategoryId = zeta.Id });
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "C2", IsPublished = true, CategoryId = alpha.Id });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetCategoriesAsync("courses", "en");

        result.Select(c => c.Name).ShouldBe(["Alpha", "Zeta"]);
    }
}
