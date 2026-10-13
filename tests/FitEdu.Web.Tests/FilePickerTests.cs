using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Shared.Resources;
using FitEdu.Web.Shared.Components.Forms;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the shared file picker: its button and "no file chosen" text follow the page
/// language (the browser's own file input does not), and the chosen file's name replaces that text.
/// </summary>
public class FilePickerTests : BunitContext
{
    private const string PickerId = "avatar";

    public FilePickerTests()
    {
        // Every component gets IConfiguration through _Imports.razor.
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void BeforeAChoice_ShowsAButtonForTheInputAndNoFileText()
    {
        var picker = Render<FilePicker>(parameters => parameters.Add(p => p.Id, PickerId));

        var input = picker.Find("input[type=file]");
        input.Id.ShouldBe(PickerId);
        var button = picker.Find("label.file-picker__button");
        button.GetAttribute("for").ShouldBe(PickerId);
        button.TextContent.Trim().ShouldBe(GlobalRes.ChooseFile);
        var fileName = picker.Find(".file-picker__name");
        fileName.TextContent.ShouldBe(GlobalRes.NoFileChosen);
        input.GetAttribute("aria-describedby").ShouldBe(fileName.Id);
    }

    [Fact]
    public void InGreek_TheButtonAndTheEmptyTextAreGreek()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");

        var picker = Render<FilePicker>(parameters => parameters.Add(p => p.Id, PickerId));

        picker.Find("label.file-picker__button").TextContent.Trim().ShouldBe("Επίλεξε αρχείο");
        picker.Find(".file-picker__name").TextContent.ShouldBe("Δεν επιλέχτηκε αρχείο");
    }

    [Fact]
    public void ChoosingAFile_ShowsItsNameAndPassesTheChangeOn()
    {
        string? receivedName = null;
        var picker = Render<FilePicker>(parameters => parameters
            .Add(p => p.Id, PickerId)
            .Add(p => p.OnChange, (InputFileChangeEventArgs args) => receivedName = args.File.Name));

        picker.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("image", "me.png"));

        picker.Find(".file-picker__name").TextContent.ShouldBe("me.png");
        receivedName.ShouldBe("me.png");
    }

    [Fact]
    public void Disabled_DisablesTheInputAndPassesTheAcceptList()
    {
        var picker = Render<FilePicker>(parameters => parameters
            .Add(p => p.Id, PickerId)
            .Add(p => p.Accept, "image/*")
            .Add(p => p.Disabled, true));

        var input = picker.Find("input[type=file]");
        input.HasAttribute("disabled").ShouldBeTrue();
        input.GetAttribute("accept").ShouldBe("image/*");
    }
}
