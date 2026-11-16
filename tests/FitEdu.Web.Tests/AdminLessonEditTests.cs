using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Pages;
using FitEdu.Web.Shared.Components.Forms;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the lesson page's add/edit dialog: a PDF or video over the limit is refused when
/// it is chosen, with a message inside the dialog, and the lesson still saves without it.
/// </summary>
public class AdminLessonEditTests : BunitContext
{
    private readonly IAdminLessonConsumer _lessonConsumer = Substitute.For<IAdminLessonConsumer>();
    private readonly IAdminModuleConsumer _moduleConsumer = Substitute.For<IAdminModuleConsumer>();
    private readonly Guid _moduleId = Guid.NewGuid();

    public AdminLessonEditTests()
    {
        Services.AddSingleton(_lessonConsumer);
        Services.AddSingleton(_moduleConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // QuillEditor and FormModal call JS on first render; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _lessonConsumer.GetLessonsByModuleAsync(_moduleId).Returns([]);
        _lessonConsumer.CreateLessonAsync(Arg.Any<SaveLessonRequest>())
            .Returns(call => new AdminLessonDto(Guid.NewGuid(), "Leg day", null, null, null, null, null, 30, 1, _moduleId, []));
    }

    [Theory]
    [InlineData("lesson-pdf", "application/pdf", UploadLimits.MaxPdfBytes)]
    [InlineData("lesson-video", "video/mp4", UploadLimits.MaxVideoBytes)]
    public void FileOverTheLimit_IsRefusedAtOnce_AndTheLessonStillSavesWithoutIt(string pickerId, string contentType, long limit)
    {
        var page = Render<AdminLessonEdit>(parameters => parameters.Add(p => p.ModuleId, _moduleId));
        page.FindAll("button").First(button => button.TextContent.Trim() == AdminRes.AddLesson).Click();
        page.Find("#lesson-title-en").Change("Leg day");
        var picker = page.FindComponents<FilePicker>().Single(candidate => candidate.Instance.Id == pickerId);

        FakeBrowserFile.Choose(picker, new FakeBrowserFile("big-file", limit + 1, contentType));

        page.Find(".modal .alert-danger").TextContent.ShouldContain(UploadSizeText.TooLarge(limit));
        picker.Find(".file-picker__name").TextContent.ShouldBe(GlobalRes.NoFileChosen);
        // A screen reader hears the error when it appears, and the picker names it when focused.
        page.Find(".modal .alert-danger").GetAttribute("role").ShouldBe("alert");
        picker.Find("input[type=file]").GetAttribute("aria-describedby")!.ShouldContain(page.Find(".modal .alert-danger").Id!);

        page.Find(".modal-footer .btn-primary").Click();

        _lessonConsumer.Received(1).CreateLessonAsync(Arg.Any<SaveLessonRequest>());
        _lessonConsumer.DidNotReceive().UploadPdfAsync(Arg.Any<Guid>(), Arg.Any<IBrowserFile>());
        _lessonConsumer.DidNotReceive().UploadVideoAsync(Arg.Any<Guid>(), Arg.Any<IBrowserFile>());
    }
}
