using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Web.Interfaces;

#pragma warning disable CA1716 // The Shared namespace is the app's component root; renaming it would break every Razor import.
namespace FitEdu.Web.Shared.Components.Call;

public partial class ActiveCallView : IDisposable
{
    [Inject] private ICallService CallService { get; set; } = default!;

    private bool _showAddPicker;

    private int RemainingSlots => Math.Max(0, CallService.MaxParticipants - 1 - CallService.Participants.Count);

    protected override void OnInitialized()
    {
        CallService.StateChanged += HandleStateChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await CallService.BindLocalVideoAsync("video-local");
        }
    }

    private void HandleStateChanged() => InvokeAsync(StateHasChanged);

    private void OpenAddParticipantPicker() => _showAddPicker = true;

    private void CloseAddParticipantPicker() => _showAddPicker = false;

    private async Task HandleAddParticipantsPicked(List<ChatUserDto> users)
    {
        _showAddPicker = false;
        foreach (var user in users)
        {
            await CallService.InviteAsync(user.Id);
        }
    }

    private async Task EndCallAsync()
    {
        if (CallService.Stage == CallStage.Outgoing)
            await CallService.CancelAsync();
        else
            await CallService.HangUpAsync();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CallService.StateChanged -= HandleStateChanged;
    }
}
