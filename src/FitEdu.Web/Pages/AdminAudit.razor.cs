using Microsoft.AspNetCore.Components;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Pages;

public partial class AdminAudit
{
    private const string NoDetailText = "—";

    private static readonly int[] PageSizeOptions = [10, 25, 50, 100];

    [Inject] private IAdminAuditConsumer AuditConsumer { get; set; } = default!;
    [Inject] private ILogger<AdminAudit> _logger { get; set; } = default!;

    private PagedResult<AdminAuditEntryDto>? pagedResult;
    private int currentPage = 1;
    private int pageSize = 10;
    private bool _loadFailed;
    private int _loadSequence;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        // Answers can arrive out of order (a second click on Next while the first page is loading):
        // only the newest request may change the page.
        var sequence = ++_loadSequence;
        _loadFailed = false;
        try
        {
            var loaded = await AuditConsumer.GetEntriesAsync(currentPage, pageSize);
            if (sequence != _loadSequence)
                return;

            // The consumer answers null for any failed request (an error status, no connection).
            if (loaded is null)
            {
                _loadFailed = true;
                return;
            }

            pagedResult = loaded;
        }
        catch (Exception ex)
        {
            if (sequence != _loadSequence)
                return;

            LogAuditLoadFailed(_logger, ex);
            _loadFailed = true;
        }
    }

    private async Task OnPageSizeChanged(int size)
    {
        pageSize = size;
        currentPage = 1;
        await LoadAsync();
    }

    private async Task PreviousPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            await LoadAsync();
        }
    }

    private async Task NextPage()
    {
        if (pagedResult is { HasNextPage: true })
        {
            currentPage++;
            await LoadAsync();
        }
    }

    // An account that still exists is shown by its email; a deleted one has only its id left in the log.
    private static string AccountText(string? email, string userId)
        => email ?? ResourceFormat.Fill(AdminRes.AuditDeletedAccountFormat, userId);

    // The detail of a role change is the role name, shown in the reader's language; other actions have none.
    private static string DetailText(AdminAuditEntryDto entry)
        => string.IsNullOrEmpty(entry.Detail) ? NoDetailText : RoleLabels.Label(entry.Detail);

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading the admin audit log.")]
    private static partial void LogAuditLoadFailed(ILogger logger, Exception exception);
}
