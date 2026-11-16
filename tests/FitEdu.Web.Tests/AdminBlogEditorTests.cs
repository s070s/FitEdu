using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the blog editor's cover image: the size check before an upload, an unreadable
/// file, the preview and the upload result after creating an article.
/// </summary>
public class AdminBlogEditorTests : BunitContext
{
    private readonly IAdminBlogConsumer _blogConsumer = Substitute.For<IAdminBlogConsumer>();
    private readonly Guid _articleId = Guid.NewGuid();

    public AdminBlogEditorTests()
    {
        Services.AddSingleton(_blogConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // QuillEditor and the unsaved-changes guard call JS; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _blogConsumer.GetArticleAsync(_articleId, Arg.Any<CancellationToken>()).Returns(Article(coverImageUrl: null));
        _blogConsumer.UpdateArticleAsync(Arg.Any<Guid>(), Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiActionResult.Success);
    }

    private AdminBlogArticleDto Article(string? coverImageUrl) => new(
        _articleId, "Title", null, "title", "Summary", null, "<p>Body</p>", null, coverImageUrl, "Coach",
        [], null, false, DateTimeOffset.UtcNow, null);

    private IRenderedComponent<AdminBlogEditor> RenderEditMode() =>
        Render<AdminBlogEditor>(parameters => parameters.Add(p => p.Id, _articleId));

    private static InputFileContent File(string name, long sizeInBytes) =>
        InputFileContent.CreateFromBinary(new byte[sizeInBytes], name);

    private static void ClickSave(IRenderedComponent<AdminBlogEditor> page) =>
        page.FindAll(".form-actions button").First(button => button.TextContent.Trim() == AdminRes.Save).Click();

    private static void FillRequiredFields(IRenderedComponent<AdminBlogEditor> page)
    {
        page.Find("#blog-title-en").Input("Beginner strength guide");
        page.Find("#blog-summary-en").Change("A short summary.");
        page.Find("#blog-author").Change("Coach");
    }

    [Fact]
    public void EditMode_StoredCover_PreviewsThroughTheMediaRoute()
    {
        _blogConsumer.GetArticleAsync(_articleId, Arg.Any<CancellationToken>()).Returns(Article("blog/covers/photo.jpg"));

        var page = RenderEditMode();

        page.Find("img.cover-preview").GetAttribute("src").ShouldBe("/api/media/blog/covers/photo.jpg");
    }

    [Fact]
    public void Cover_HasNoFreeTextUrlBox_SoOnlyUploadedSiteFilesCanBeCovers()
    {
        _blogConsumer.GetArticleAsync(_articleId, Arg.Any<CancellationToken>()).Returns(Article("blog/covers/photo.jpg"));

        var page = RenderEditMode();

        // The page's CSP blocks images from other sites, so an invited outside URL would never show.
        page.Markup.ShouldNotContain("paste an image URL", Case.Insensitive);
        page.FindAll("input[type=text]").Any(input => input.GetAttribute("value") == "blog/covers/photo.jpg").ShouldBeFalse();
    }

    [Fact]
    public void EditMode_AfterAnUpload_PreviewsTheNewPathThroughTheMediaRoute()
    {
        _blogConsumer.UploadCoverImageAsync(_articleId, Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>())
            .Returns("blog/covers/new.jpg");
        var page = RenderEditMode();

        page.FindComponent<InputFile>().UploadFiles(File("new.jpg", 1024));

        page.Find("img.cover-preview").GetAttribute("src").ShouldBe("/api/media/blog/covers/new.jpg");
    }

    [Fact]
    public void CreateMode_PendingCover_ShowsItsNameWithoutABrokenPreview()
    {
        var page = Render<AdminBlogEditor>();

        page.FindComponent<InputFile>().UploadFiles(File("holiday.jpg", 1024));

        page.FindAll("img.cover-preview").ShouldBeEmpty();
        page.Find(".cover-preview-row").TextContent.ShouldContain("holiday.jpg");
        page.Find(".cover-preview-row button").TextContent.ShouldBe(AdminRes.Remove);
    }

    [Fact]
    public void CreateMode_PendingCover_IsNotSentAsTheCoverUrlWhenSaving()
    {
        SaveBlogArticleRequest? sent = null;
        _blogConsumer.CreateArticleAsync(Arg.Do<SaveBlogArticleRequest>(request => sent = request), Arg.Any<CancellationToken>())
            .Returns(Article(null));
        _blogConsumer.UploadCoverImageAsync(_articleId, Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>())
            .Returns("blog/covers/holiday.jpg");
        var page = Render<AdminBlogEditor>();
        FillRequiredFields(page);
        page.FindComponent<InputFile>().UploadFiles(File("holiday.jpg", 1024));

        ClickSave(page);

        sent.ShouldNotBeNull();
        sent.CoverImageUrl.ShouldBeNull();
        _blogConsumer.Received(1).UploadCoverImageAsync(_articleId, Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateMode_CoverUploadReturnsNothing_ShowsTheUploadFailedMessageAndStaysToRetry()
    {
        _blogConsumer.CreateArticleAsync(Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>()).Returns(Article(null));
        _blogConsumer.UploadCoverImageAsync(_articleId, Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var page = Render<AdminBlogEditor>();
        FillRequiredFields(page);
        page.FindComponent<InputFile>().UploadFiles(File("holiday.jpg", 1024));

        ClickSave(page);

        page.Markup.ShouldContain(AdminRes.CoverImageUploadFailed);
        new Uri(navigation.Uri).AbsolutePath.ShouldNotBe("/admin/blog");
        page.Find(".file-picker__name").TextContent.ShouldBe(GlobalRes.NoFileChosen);

        // The article exists now: saving again updates it instead of creating a second one.
        ClickSave(page);

        _blogConsumer.Received(1).CreateArticleAsync(Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>());
        _blogConsumer.Received(1).UpdateArticleAsync(_articleId, Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditMode_CoverWithSpacesBeforeADoubleSlash_IsNotSaved()
    {
        _blogConsumer.GetArticleAsync(_articleId, Arg.Any<CancellationToken>()).Returns(Article(" //host/x.png"));
        var page = RenderEditMode();

        ClickSave(page);

        page.Find("#blog-error").TextContent.ShouldBe(AdminRes.CoverMustBeSiteFile);
        _blogConsumer.DidNotReceive().UpdateArticleAsync(Arg.Any<Guid>(), Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditMode_OutsideCoverUrl_IsNotSaved_UntilItIsRemoved()
    {
        _blogConsumer.GetArticleAsync(_articleId, Arg.Any<CancellationToken>()).Returns(Article("https://x/y.jpg"));
        var page = RenderEditMode();

        ClickSave(page);

        page.Find("#blog-error").TextContent.ShouldBe(AdminRes.CoverMustBeSiteFile);
        _blogConsumer.DidNotReceive().UpdateArticleAsync(Arg.Any<Guid>(), Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>());

        page.Find(".cover-preview-row button").Click();
        ClickSave(page);

        _blogConsumer.Received(1).UpdateArticleAsync(_articleId, Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditMode_UpdateRefusedWithADetail_ShowsThatDetail()
    {
        _blogConsumer.UpdateArticleAsync(_articleId, Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiActionResult.Refused(System.Net.HttpStatusCode.Conflict, ErrorMessagesRes.ModifiedConcurrently));
        var page = RenderEditMode();

        ClickSave(page);

        page.Find("#blog-error").TextContent.ShouldBe(ErrorMessagesRes.ModifiedConcurrently);
    }

    [Fact]
    public void EditMode_UpdateRefusedWithoutADetail_ShowsTheSlugInUseMessage()
    {
        _blogConsumer.UpdateArticleAsync(_articleId, Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiActionResult.Refused(System.Net.HttpStatusCode.BadRequest));
        var page = RenderEditMode();

        ClickSave(page);

        page.Find("#blog-error").TextContent.ShouldBe(AdminRes.SlugInUse);
    }

    [Fact]
    public void CreateMode_CoverThatCannotBeReadAfterSaving_ShowsTheUploadFailedMessage()
    {
        _blogConsumer.CreateArticleAsync(Arg.Any<SaveBlogArticleRequest>(), Arg.Any<CancellationToken>()).Returns(Article(null));
        _blogConsumer.UploadCoverImageAsync(_articleId, Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("The file changed while it was read."));
        var page = Render<AdminBlogEditor>();
        FillRequiredFields(page);
        page.FindComponent<InputFile>().UploadFiles(File("holiday.jpg", 1024));

        ClickSave(page);

        page.Markup.ShouldContain(AdminRes.CoverImageUploadFailed);
    }

    [Fact]
    public void EditMode_CoverLargerThanTheLimit_ShowsTheTooLargeMessageAndUploadsNothing()
    {
        var page = RenderEditMode();

        page.FindComponent<InputFile>().UploadFiles(File("big.jpg", UploadLimits.MaxCoverImageBytes + 1));

        page.Markup.ShouldContain(UploadSizeText.TooLarge(UploadLimits.MaxCoverImageBytes));
        page.Find(".file-picker__name").TextContent.ShouldBe(GlobalRes.NoFileChosen);
        _blogConsumer.DidNotReceive().UploadCoverImageAsync(Arg.Any<Guid>(), Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UploadError_IsAnnouncedAndLinkedToThePicker()
    {
        var page = RenderEditMode();
        page.FindAll("[role=alert]").ShouldBeEmpty();

        page.FindComponent<InputFile>().UploadFiles(File("big.jpg", UploadLimits.MaxCoverImageBytes + 1));

        var alert = page.Find("[role=alert]");
        alert.TextContent.ShouldContain(UploadSizeText.TooLarge(UploadLimits.MaxCoverImageBytes));
        alert.Id.ShouldNotBeNullOrEmpty();
        page.Find("input[type=file]").GetAttribute("aria-describedby")!.Split(' ').ShouldContain(alert.Id);
    }

    [Fact]
    public void EditMode_CoverThatCannotBeRead_ShowsTheUploadFailedMessageInsteadOfThrowing()
    {
        _blogConsumer.UploadCoverImageAsync(_articleId, Arg.Any<IBrowserFile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("The file changed while it was read."));
        var page = RenderEditMode();

        Should.NotThrow(() => page.FindComponent<InputFile>().UploadFiles(File("cover.jpg", 1024)));

        page.Markup.ShouldContain(AdminRes.CoverImageUploadFailed);
        page.Find(".file-picker__name").TextContent.ShouldBe(GlobalRes.NoFileChosen);
    }
}
