using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
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
    }

    private AdminBlogArticleDto Article(string? coverImageUrl) => new(
        _articleId, "Title", null, "title", "Summary", null, "<p>Body</p>", null, coverImageUrl, "Coach",
        [], null, false, DateTimeOffset.UtcNow, null);

    private IRenderedComponent<AdminBlogEditor> RenderEditMode() =>
        Render<AdminBlogEditor>(parameters => parameters.Add(p => p.Id, _articleId));

    private static InputFileContent File(string name, long sizeInBytes) =>
        InputFileContent.CreateFromBinary(new byte[sizeInBytes], name);

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
