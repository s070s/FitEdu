using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Web.Shared.Components.Data;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// modalInterop.activate remembers the element that has focus as the trigger to give focus back to when the dialog
/// closes, so it has to run before the dialog takes the focus; after it, the dialog itself would be recorded.
/// </summary>
public class ModalFocusTests : BunitContext
{
    private const string ActivateIdentifier = "modalInterop.activate";
    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    private bool _dialogHadFocusWhenActivated;

    public ModalFocusTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        JSInterop.Mode = JSRuntimeMode.Loose;
        // bUnit answers ElementReference.FocusAsync itself, so the order is read from what has been recorded
        // by the time activate runs.
        JSInterop.SetupVoid(ActivateIdentifier, _ =>
        {
            _dialogHadFocusWhenActivated = JSInterop.Invocations[FocusIdentifier].Count > 0;
            return true;
        }).SetVoidResult();
    }

    [Fact]
    public void ConfirmModal_RecordsTheTriggerBeforeTheDialogTakesTheFocus()
    {
        var modal = Render<ConfirmModal>(parameters => parameters.Add(component => component.IsVisible, true));

        // The focus call follows the awaited activate call, so it can land after Render returns.
        modal.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
        JSInterop.VerifyInvoke(ActivateIdentifier);
        _dialogHadFocusWhenActivated.ShouldBeFalse();
    }

    [Fact]
    public void FormModal_RecordsTheTriggerBeforeTheDialogTakesTheFocus()
    {
        var modal = Render<FormModal>(parameters => parameters
            .Add(component => component.IsVisible, true)
            .Add(component => component.Title, "Title")
            .Add(component => component.Body, (RenderFragment)(builder => builder.AddContent(0, "Body"))));

        // The focus call follows the awaited activate call, so it can land after Render returns.
        modal.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
        JSInterop.VerifyInvoke(ActivateIdentifier);
        _dialogHadFocusWhenActivated.ShouldBeFalse();
    }
}
