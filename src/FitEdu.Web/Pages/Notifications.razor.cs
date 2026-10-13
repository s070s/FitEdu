using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Web.Consumers;
using FitEdu.Web.Layout;

namespace FitEdu.Web.Pages;

public partial class Notifications
{
    [Inject] private INotificationConsumer Consumer { get; set; } = default!;

    // Sort choices of the select, each a (column, direction) pair the notifications API accepts.
    private const string SortNewest = "newest";
    private const string SortOldest = "oldest";
    private const string SortUnreadFirst = "unread";
    private const string CreatedColumn = "createdat";
    private const string ReadColumn = "isread";
    private const string Ascending = "asc";
    private const string Descending = "desc";

    private PagedResult<NotificationDto>? pagedResult;
    private int currentPage = 1;
    private int pageSize = 10;
    private static readonly int[] PageSizeOptions = [10, 25, 50];
    private string _sortBy = CreatedColumn;
    private string _sortDir = Descending;

    private string SelectedSortOption => (_sortBy, _sortDir) switch
    {
        (ReadColumn, _) => SortUnreadFirst,
        (_, Ascending) => SortOldest,
        _ => SortNewest
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task OnSortChanged(ChangeEventArgs e)
    {
        // Unread first sorts IsRead ascending: false (unread) comes before true (read).
        (_sortBy, _sortDir) = e.Value?.ToString() switch
        {
            SortOldest => (CreatedColumn, Ascending),
            SortUnreadFirst => (ReadColumn, Ascending),
            _ => (CreatedColumn, Descending)
        };
        currentPage = 1;
        await LoadAsync();
    }

    /// <summary>The Bootstrap Icons glyph for a notification, by its <c>NotificationType</c> name.</summary>
    private static string IconFor(string notificationType) => notificationType switch
    {
        nameof(NotificationType.ChatMessage) => AppIcons.Chat,
        nameof(NotificationType.CertificateIssued) => AppIcons.Certificate,
        nameof(NotificationType.SubscriptionActivated) => AppIcons.Billing,
        nameof(NotificationType.SubscriptionExpiring) => AppIcons.Billing,
        nameof(NotificationType.ReviewApproved) => AppIcons.Reviews,
        nameof(NotificationType.SessionReminder) => AppIcons.Session,
        _ => "bi-bell"
    };

    private async Task LoadAsync()
    {
        pagedResult = await Consumer.GetNotificationsAsync(currentPage, pageSize, _sortBy, _sortDir);
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

    private async Task MarkReadAsync(Guid id)
    {
        if (await Consumer.MarkReadAsync(id))
            await LoadAsync();
    }

    private async Task MarkAllReadAsync()
    {
        if (await Consumer.MarkAllReadAsync())
            await LoadAsync();
    }
}
