using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using FitEdu.Web.Controllers;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// P05-6: a replacing upload saves the new file, updates the database, and only then deletes the old file. A save that
/// throws keeps the old file and the old value; a delete that throws is not an error; a value that is a URL or a site
/// path is never deleted; and a deleted lesson loses its files only after the delete is saved.
/// </summary>
public sealed class UploadReplacementOrderTests : IDisposable
{
    private const string OldPath = "blog/covers/old.png";
    private const string NewPath = "blog/covers/0123456789abcdef0123456789abcdef.png";

    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly ApplicationDbContext _db;
    private readonly ApplicationDbContext _observer;
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();

    public UploadReplacementOrderTests()
    {
        _db = DbContextFactory.CreateInMemory(_databaseName);
        _observer = DbContextFactory.CreateInMemory(_databaseName); // a second view of the store: what is really saved
    }

    public void Dispose()
    {
        _db.Dispose();
        _observer.Dispose();
    }

    private void StorageSavesAs(string path) =>
        _storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(path);

    private void StorageSaveThrows() =>
        _storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));

    private static FormFile Png(string contentType = "image/png")
    {
        var bytes = UploadSamples.Png();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "x.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static FormFile Pdf()
    {
        var bytes = UploadSamples.Pdf();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "x.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static FormFile Mp4()
    {
        var bytes = UploadSamples.Mp4();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "x.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4"
        };
    }

    private static T WithContext<T>(T controller) where T : ControllerBase
    {
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = admin } };
        controller.ProblemDetailsFactory = new PlainProblemDetailsFactory();
        return controller;
    }

    // ── Blog cover ──

    private async Task<(AdminBlogController Controller, Guid ArticleId)> BlogWithACoverAsync(string? cover)
    {
        var article = new BlogArticle
        {
            Id = Guid.NewGuid(), TitleEn = "T", Slug = "s-" + Guid.NewGuid().ToString("N"), SummaryEn = "S", ContentEn = "C",
            AuthorName = "A", CoverImageUrl = cover
        };
        _db.BlogArticles.Add(article);
        await _db.SaveChangesAsync();
        var controller = WithContext(new AdminBlogController(
            Substitute.For<IBlogArticleService>(), _storage, _db, NullLogger<AdminBlogController>.Instance));
        return (controller, article.Id);
    }

    [Fact]
    public async Task BlogCover_SaveFails_KeepsTheOldFileAndTheOldValue()
    {
        var (controller, articleId) = await BlogWithACoverAsync(OldPath);
        StorageSaveThrows();

        await Should.ThrowAsync<IOException>(() => controller.UploadBlogCoverImage(articleId, Png()));

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _observer.BlogArticles.AsNoTracking().SingleAsync(a => a.Id == articleId)).CoverImageUrl.ShouldBe(OldPath);
    }

    [Fact]
    public async Task BlogCover_DeletesTheOldFileOnlyAfterTheArticleIsUpdated()
    {
        var (controller, articleId) = await BlogWithACoverAsync(OldPath);
        StorageSavesAs(NewPath);
        string? savedValueWhenTheOldFileWasDeleted = null;
        _storage.When(storage => storage.DeleteFileAsync(OldPath, Arg.Any<CancellationToken>()))
            .Do(_ => savedValueWhenTheOldFileWasDeleted = _observer.BlogArticles.AsNoTracking().Single(a => a.Id == articleId).CoverImageUrl);

        var result = await controller.UploadBlogCoverImage(articleId, Png());

        result.ShouldBeOfType<OkObjectResult>();
        savedValueWhenTheOldFileWasDeleted.ShouldBe(NewPath);
    }

    [Theory]
    [InlineData("https://example.com/cover.png")]
    [InlineData("/images/blog/cover.webp")]
    public async Task BlogCover_OldValueThatIsNotAStoredFile_IsNeverDeleted(string oldValue)
    {
        var (controller, articleId) = await BlogWithACoverAsync(oldValue);
        StorageSavesAs(NewPath);

        (await controller.UploadBlogCoverImage(articleId, Png())).ShouldBeOfType<OkObjectResult>();

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BlogCover_DeletingTheOldFileFails_StillAnswersOk()
    {
        var (controller, articleId) = await BlogWithACoverAsync(OldPath);
        StorageSavesAs(NewPath);
        _storage.DeleteFileAsync(OldPath, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("file in use"));

        (await controller.UploadBlogCoverImage(articleId, Png())).ShouldBeOfType<OkObjectResult>();
    }

    // ── Testimonial avatar ──

    private AdminTestimonialsController TestimonialWithAnAvatar(Guid id, ITestimonialService testimonials, string avatar)
    {
        testimonials.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new AdminTestimonialDto(
            id, "Name", null, null, "Quote", avatar, 1, true, DateTimeOffset.UtcNow, null));
        return WithContext(new AdminTestimonialsController(testimonials, _storage, NullLogger<AdminTestimonialsController>.Instance));
    }

    [Fact]
    public async Task TestimonialAvatar_SaveFails_KeepsTheOldFileAndTheOldValue()
    {
        var id = Guid.NewGuid();
        var testimonials = Substitute.For<ITestimonialService>();
        var controller = TestimonialWithAnAvatar(id, testimonials, "testimonials/avatars/old.png");
        StorageSaveThrows();

        await Should.ThrowAsync<IOException>(() => controller.UploadAvatar(id, Png()));

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await testimonials.DidNotReceive().SetAvatarPathAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestimonialAvatar_SavesThenUpdatesThenDeletesTheOldFile()
    {
        var id = Guid.NewGuid();
        var testimonials = Substitute.For<ITestimonialService>();
        var controller = TestimonialWithAnAvatar(id, testimonials, "testimonials/avatars/old.png");
        StorageSavesAs("testimonials/avatars/0123456789abcdef0123456789abcdef.png");

        (await controller.UploadAvatar(id, Png())).ShouldBeOfType<OkObjectResult>();

        Received.InOrder(() =>
        {
            _storage.SaveFileAsync(Arg.Any<Stream>(), "image/png", "testimonials/avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>());
            testimonials.SetAvatarPathAsync(id, Arg.Any<string>(), Arg.Any<CancellationToken>());
            _storage.DeleteFileAsync("testimonials/avatars/old.png", Arg.Any<CancellationToken>());
        });
    }

    // ── Lesson PDF and video ──

    private async Task<(AdminLessonsController Controller, Lesson Lesson)> LessonsWithAFilesAsync(string? pdf, string? video)
    {
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = Guid.NewGuid(), PdfPath = pdf, VideoPath = video };
        _db.Lessons.Add(lesson);
        await _db.SaveChangesAsync();
        var controller = WithContext(new AdminLessonsController(
            _db, _storage, Substitute.For<IAdminCourseService>(), NullLogger<AdminLessonsController>.Instance));
        return (controller, lesson);
    }

    [Fact]
    public async Task LessonPdf_SaveFails_KeepsTheOldFileAndTheOldValue()
    {
        var (controller, lesson) = await LessonsWithAFilesAsync("lessons/pdf/old.pdf", null);
        StorageSaveThrows();

        await Should.ThrowAsync<IOException>(() => controller.UploadPdf(lesson.Id, Pdf()));

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _observer.Lessons.AsNoTracking().SingleAsync(l => l.Id == lesson.Id)).PdfPath.ShouldBe("lessons/pdf/old.pdf");
    }

    [Fact]
    public async Task LessonPdf_DeletesTheOldFileOnlyAfterTheLessonIsUpdated()
    {
        var (controller, lesson) = await LessonsWithAFilesAsync("lessons/pdf/old.pdf", null);
        StorageSavesAs("lessons/pdf/0123456789abcdef0123456789abcdef.pdf");
        string? savedValueWhenTheOldFileWasDeleted = null;
        _storage.When(storage => storage.DeleteFileAsync("lessons/pdf/old.pdf", Arg.Any<CancellationToken>()))
            .Do(_ => savedValueWhenTheOldFileWasDeleted = _observer.Lessons.AsNoTracking().Single(l => l.Id == lesson.Id).PdfPath);

        (await controller.UploadPdf(lesson.Id, Pdf())).ShouldBeOfType<OkObjectResult>();

        savedValueWhenTheOldFileWasDeleted.ShouldBe("lessons/pdf/0123456789abcdef0123456789abcdef.pdf");
    }

    [Fact]
    public async Task LessonVideo_SaveFails_KeepsTheOldFileAndTheOldValue()
    {
        var (controller, lesson) = await LessonsWithAFilesAsync(null, "lessons/video/old.mp4");
        StorageSaveThrows();

        await Should.ThrowAsync<IOException>(() => controller.UploadVideo(lesson.Id, Mp4()));

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _observer.Lessons.AsNoTracking().SingleAsync(l => l.Id == lesson.Id)).VideoPath.ShouldBe("lessons/video/old.mp4");
    }

    [Fact]
    public async Task LessonVideo_OldValueThatIsAUrl_IsNeverDeleted()
    {
        var (controller, lesson) = await LessonsWithAFilesAsync(null, "https://www.youtube.com/embed/abc");
        StorageSavesAs("lessons/video/0123456789abcdef0123456789abcdef.mp4");

        (await controller.UploadVideo(lesson.Id, Mp4())).ShouldBeOfType<OkObjectResult>();

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteLesson_DeletesItsFilesOnlyAfterTheLessonIsGone_AndSkipsAVideoUrl()
    {
        var (controller, lesson) = await LessonsWithAFilesAsync("lessons/pdf/old.pdf", "https://www.youtube.com/embed/abc");
        var lessonStillStoredWhenAFileWasDeleted = new List<bool>();
        _storage.When(storage => storage.DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => lessonStillStoredWhenAFileWasDeleted.Add(_observer.Lessons.AsNoTracking().Any(l => l.Id == lesson.Id)));

        (await controller.DeleteLesson(lesson.Id)).ShouldBeOfType<NoContentResult>();

        lessonStillStoredWhenAFileWasDeleted.ShouldBe([false]);
        await _storage.Received(1).DeleteFileAsync("lessons/pdf/old.pdf", Arg.Any<CancellationToken>());
    }

    // ── Site background ──

    private async Task<SiteSettingsController> SettingsWithABackgroundAsync(string oldPath)
    {
        _db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = SiteSetting.LandingBackgroundImageKey, Value = oldPath, UpdatedByUserId = "admin"
        });
        await _db.SaveChangesAsync();
        return WithContext(new SiteSettingsController(_db, _storage, NullLogger<SiteSettingsController>.Instance));
    }

    [Fact]
    public async Task Background_SaveFails_KeepsTheOldFileAndTheOldValue()
    {
        var controller = await SettingsWithABackgroundAsync("backgrounds/old.png");
        StorageSaveThrows();

        await Should.ThrowAsync<IOException>(() => controller.UploadBackgroundImage(Png()));

        await _storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _observer.SiteSettings.AsNoTracking().SingleAsync()).Value.ShouldBe("backgrounds/old.png");
    }

    [Fact]
    public async Task Background_DeletesTheOldFileOnlyAfterTheSettingIsUpdated()
    {
        var controller = await SettingsWithABackgroundAsync("backgrounds/old.png");
        StorageSavesAs("backgrounds/0123456789abcdef0123456789abcdef.png");
        string? savedValueWhenTheOldFileWasDeleted = null;
        _storage.When(storage => storage.DeleteFileAsync("backgrounds/old.png", Arg.Any<CancellationToken>()))
            .Do(_ => savedValueWhenTheOldFileWasDeleted = _observer.SiteSettings.AsNoTracking().Single().Value);

        (await controller.UploadBackgroundImage(Png())).ShouldBeOfType<OkObjectResult>();

        savedValueWhenTheOldFileWasDeleted.ShouldBe("backgrounds/0123456789abcdef0123456789abcdef.png");
    }

    /// <summary>Builds plain problem details, as the app's factory does, without a service provider.</summary>
    private sealed class PlainProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext,
            ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null, string? type = null,
            string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };
    }
}
