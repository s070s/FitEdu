using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Application.Extensions;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// Admin-only user and role management.
/// </summary>
public partial class AdminUserService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ITokenService tokenService,
    ILogger<AdminUserService> logger,
    IApplicationDbContext context,
    IEmailService emailService,
    IFileStorage fileStorage,
    ILiveConnectionCloser liveConnectionCloser,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    IOptions<AppOptions> appOptions) : IAdminUserService
{
    private static readonly SemaphoreSlim AdminRoleGate = new(1, 1);

    // LOG-4: the acting admin's id for audit attribution ("who did it"), from the request principal.
    private string ActingAdmin => currentUser.UserId ?? "unknown";

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(
        int page, int pageSize, string? search, string sortBy, string sortDir, CancellationToken cancellationToken = default)
    {
        var query = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.ApplySearch(search.Trim());
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var users = await query
            .ApplySort(sortBy, sortDir)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Single query: fetch all (userId → roleName) pairs for the current page
        var userIds = users.Select(u => u.Id).ToList();
        var userRolePairs = await context.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(context.Roles,
                   ur => ur.RoleId,
                   r => r.Id,
                   (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);

        var userRoleMap = userRolePairs
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        // Single query: active subscription tier per user; users without one are Free
        var userTierPairs = await context.UserSubscriptions
            .Where(us => userIds.Contains(us.UserId) && us.IsActive)
            .Select(us => new { us.UserId, us.SubscriptionPlan.Tier })
            .ToListAsync(cancellationToken);

        var userTierMap = userTierPairs.ToDictionary(x => x.UserId, x => x.Tier);

        var result = users.Select(user =>
        {
            var roles = userRoleMap.TryGetValue(user.Id, out var r) ? r : [];
            var tier = userTierMap.TryGetValue(user.Id, out var t) ? t : SubscriptionTier.Free;
            return new AdminUserDto(
                user.Id,
                user.Email!,
                user.FirstName,
                user.LastName,
                user.DisplayName,
                user.EmailConfirmed,
                user.IsEnabled,
                user.Status.ToString(),
                [.. roles],
                tier,
                user.CreatedAt,
                user.LastSeenAt
            );
        }).ToList();

        return new PagedResult<AdminUserDto>(result, totalCount, page, pageSize, sortBy, sortDir);
    }

    public async Task<AdminUserDetailDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        return new AdminUserDetailDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.AgeOn(SiteClock.Today(timeProvider)),
            user.Status.ToString(),
            user.EmailConfirmed,
            user.IsEnabled,
            user.GdprConsentGiven,
            user.GdprConsentDate,
            user.ParentalConsentGiven,
            user.CreatedAt,
            [.. roles],
            user.Goal?.ToString());
    }

    public async Task<ServiceResult<string>> AssignRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<string>.NotFound(error: "User not found.");

        if (!await roleManager.RoleExistsAsync(roleName))
            return ServiceResult<string>.BadRequest(error: $"Role '{roleName}' does not exist.");

        if (await userManager.IsInRoleAsync(user, roleName))
            return ServiceResult<string>.BadRequest(error: $"User already has role '{roleName}'.");

        var audit = StageAudit(AdminAuditAction.RoleAdded, userId, roleName);
        var result = await userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            DiscardAudit(audit);
            return ServiceResult<string>.BadRequest(error: string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        // The roles travel in the token and the cookie, so the old sessions would keep the old roles.
        var sessionsEnded = await EndSessionsAsync(user, cancellationToken);
        // The role is saved already, so a retry would be refused (the user has or lacks the role): the admin is told what to do instead.
        if (!sessionsEnded.Succeeded)
            return ServiceResult<string>.Conflict(error: ErrorMessagesRes.RoleChangedSessionsNotEnded);

        LogRoleAssigned(logger, ActingAdmin, roleName, userId);
        return ServiceResult<string>.Ok($"Role '{roleName}' assigned.");
    }

    public async Task<ServiceResult<string>> RemoveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<string>.NotFound(error: "User not found.");

        if (!await userManager.IsInRoleAsync(user, roleName))
            return ServiceResult<string>.BadRequest(error: $"User does not have role '{roleName}'.");

        var audit = StageAudit(AdminAuditAction.RoleRemoved, userId, roleName);
        var result = string.Equals(roleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase)
            ? await RemoveAdminRoleAsync(user, cancellationToken)
            : await userManager.RemoveFromRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            DiscardAudit(audit);
            return ServiceResult<string>.BadRequest(error: string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        var sessionsEnded = await EndSessionsAsync(user, cancellationToken);
        // The role is saved already, so a retry would be refused (the user has or lacks the role): the admin is told what to do instead.
        if (!sessionsEnded.Succeeded)
            return ServiceResult<string>.Conflict(error: ErrorMessagesRes.RoleChangedSessionsNotEnded);

        LogRoleRemoved(logger, ActingAdmin, roleName, userId);
        return ServiceResult<string>.Ok($"Role '{roleName}' removed.");
    }

    // Without an enabled admin nobody could manage the site or hand the role out again. The check and the removal
    // run one at a time, so two admins removed at once cannot both see the other one still there. The gate is per
    // process, which is the single instance this site runs on.
    private async Task<IdentityResult> RemoveAdminRoleAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (user.Id == ActingAdmin)
            return Refused("You cannot remove the Admin role from your own account.");

        await AdminRoleGate.WaitAsync(cancellationToken);
        try
        {
            var admins = await userManager.GetUsersInRoleAsync(RoleNames.Admin);
            if (!admins.Any(admin => admin.IsEnabled && admin.Id != user.Id))
                return Refused("The last Admin account keeps its Admin role.");

            return await userManager.RemoveFromRoleAsync(user, RoleNames.Admin);
        }
        finally
        {
            AdminRoleGate.Release();
        }
    }

    private static IdentityResult Refused(string description) => IdentityResult.Failed(new IdentityError { Description = description });

    public async Task<List<string?>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await roleManager.Roles.Select(r => r.Name).ToListAsync(cancellationToken);
    }

    public async Task<ServiceResult<string>> CreateRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        if (await roleManager.RoleExistsAsync(roleName))
            return ServiceResult<string>.BadRequest(error: $"Role '{roleName}' already exists.");

        var result = await roleManager.CreateAsync(new IdentityRole(roleName));
        if (!result.Succeeded)
            return ServiceResult<string>.BadRequest(error: string.Join(", ", result.Errors.Select(e => e.Description)));

        LogRoleCreated(logger, ActingAdmin, roleName);
        return ServiceResult<string>.Ok($"Role '{roleName}' created.");
    }

    public async Task<ServiceResult<string>> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<string>.NotFound(error: "User not found.");

        if (await userManager.IsInRoleAsync(user, RoleNames.Admin))
            return ServiceResult<string>.BadRequest(error: "Admin accounts cannot be deleted.");

        // Chat and call history rows reference the user with Restrict FKs, so a bare
        // Identity delete throws for any user who ever chatted or joined a call.
        // Stage the history cleanup in the change tracker; userManager.DeleteAsync shares
        // this scoped DbContext, so its SaveChanges flushes the cleanup and the user row
        // in a single transaction. Remaining user data (subscriptions, tokens, submissions,
        // lesson completions) is removed by the database cascades.
        var avatarPath = user.AvatarPath;
        var conversations = await context.ChatConversations
            .Where(c => c.CreatorId == userId || c.ParticipantId == userId)
            .ToListAsync(cancellationToken);
        var conversationIds = conversations.Select(c => c.Id).ToList();

        // Messages the user sent anywhere, plus every message inside the conversations
        // being removed (the other party's messages go with their conversation).
        var messages = await context.ChatMessages
            .Where(m => m.SenderId == userId || conversationIds.Contains(m.ConversationId))
            .ToListAsync(cancellationToken);

        var initiatedSessions = await context.CallSessions
            .Where(s => s.InitiatorId == userId)
            .ToListAsync(cancellationToken);
        var initiatedSessionIds = initiatedSessions.Select(s => s.Id).ToList();

        var callParticipants = await context.CallParticipants
            .Where(p => p.UserId == userId || initiatedSessionIds.Contains(p.CallSessionId))
            .ToListAsync(cancellationToken);

        // Certificates must be deleted before their enrollments: Certificate→Enrollment is
        // NoAction, so the User→Enrollment cascade alone can trip over surviving certificate
        // rows (cascade ordering across NoAction FKs is not guaranteed on SQL Server).
        var certificates = await context.Certificates
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);

        // The PDFs carry the recipient's name, so their files must go with the certificate rows.
        var certificatePdfPaths = certificates
            .SelectMany(c => new[] { c.PdfPath, c.PdfPathEl })
            .OfType<string>()
            .Where(path => path.Length > 0)
            .ToList();

        var enrollments = await context.Enrollments
            .Where(e => e.UserId == userId)
            .ToListAsync(cancellationToken);

        context.ChatMessages.RemoveRange(messages);
        context.ChatConversations.RemoveRange(conversations);
        context.CallParticipants.RemoveRange(callParticipants);
        context.CallSessions.RemoveRange(initiatedSessions);
        context.Certificates.RemoveRange(certificates);
        context.Enrollments.RemoveRange(enrollments);

        // The row goes into the same save as the deletion. A member erasing their own account is not an admin
        // action, so it has no row. The row keeps only the ids, so it outlives the account it names.
        var audit = userId == ActingAdmin ? null : StageAudit(AdminAuditAction.AccountDeleted, userId);
        try
        {
            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                DiscardAudit(audit);
                return ServiceResult<string>.BadRequest(error: string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        catch (DbUpdateException ex)
        {
            DiscardAudit(audit);
            // A member erasing their own account (ProfileController) is not an admin action in the log.
            if (userId == ActingAdmin)
                LogOwnAccountErasureConstraintFailed(logger, ex, userId);
            else
                LogDeleteUserConstraintFailed(logger, ex, ActingAdmin, userId);
            return ServiceResult<string>.Conflict(error: "User could not be deleted because other data still references the account.");
        }

        // The account is gone, so its profile photo goes too: a file kept on disk would outlive the erasure.
        if (!string.IsNullOrEmpty(avatarPath))
            await DeleteAvatarFileAsync(avatarPath, userId, cancellationToken);

        await DeleteCertificatePdfsAsync(certificatePdfPaths, userId, cancellationToken);

        if (userId == ActingAdmin)
            LogAccountErasedByOwner(logger, userId);
        else
            LogUserDeleted(logger, ActingAdmin, userId);

        return ServiceResult<string>.Ok("User deleted.");
    }

    // The deletion has already happened here, so a file that cannot be removed is logged, not reported as a failure.
    private async Task DeleteAvatarFileAsync(string avatarPath, string userId, CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteFileAsync(avatarPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogAvatarFileNotDeleted(logger, ex, userId);
        }
    }

    // Same rule as the avatar: the account is gone, so a PDF that cannot be removed is logged and the others still go.
    private async Task DeleteCertificatePdfsAsync(IReadOnlyList<string> pdfPaths, string userId, CancellationToken cancellationToken)
    {
        foreach (var pdfPath in pdfPaths)
        {
            try
            {
                await fileStorage.DeleteFileAsync(pdfPath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LogCertificatePdfNotDeleted(logger, ex, userId);
            }
        }
    }

    public async Task<IEnumerable<AdminUserSearchResultDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        var users = await userManager.Users
            .AsNoTracking()
            .ApplySearch(query.Trim())
            .Take(50)
            .ToListAsync(cancellationToken);

        // Single JOIN query for all roles — avoids N+1 round-trips.
        var userIds = users.Select(u => u.Id).ToList();
        var userRolePairs = await context.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(context.Roles,
                   ur => ur.RoleId,
                   r => r.Id,
                   (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);

        var userRoleMap = userRolePairs
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        return users.Select(user => new AdminUserSearchResultDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.EmailConfirmed,
            userRoleMap.TryGetValue(user.Id, out var r) ? r : new List<string>()
        ));
    }

    public async Task<ServiceResult<bool>> ForcePasswordResetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<bool>.NotFound(error: "User not found");

        if (string.IsNullOrEmpty(user.Email))
            return ServiceResult<bool>.BadRequest(error: "User has no email address.");

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = AuthLinks.ResetPassword(appOptions.Value.BaseUrl, user.Email, token);

        // The admin may read the site in another language; the member gets the email in their own.
        await emailService.SendPasswordResetAsync(user.Email, resetUrl, user.PreferredLanguage, cancellationToken);
        LogForcedPasswordResetTriggered(logger, ActingAdmin, userId);

        return ServiceResult<bool>.NoContent();
    }

    public async Task<ServiceResult<bool>> DisableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<bool>.NotFound(error: "User not found");

        if (user.Id == ActingAdmin)
            return ServiceResult<bool>.BadRequest(error: "You cannot disable your own account.");

        if (await userManager.IsInRoleAsync(user, RoleNames.Admin))
            return ServiceResult<bool>.BadRequest(error: "Admin accounts cannot be disabled.");

        // IsEnabled is the flag every session check reads (cookie, bearer token, refresh, hubs); a lock-out
        // date alone left the member signed in. It is saved by the stamp update, in the same UPDATE, so a
        // failure cannot leave the flag set with the old sessions still valid.
        var sessionsEnded = await EndSessionsAsync(user, cancellationToken, endedUser => endedUser.IsEnabled = false, AdminAuditAction.UserDisabled);
        if (!sessionsEnded.Succeeded)
            return IdentityFailure<bool>(sessionsEnded);

        LogUserDisabled(logger, ActingAdmin, userId);
        return ServiceResult<bool>.NoContent();
    }

    public async Task<ServiceResult<bool>> EnableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<bool>.NotFound(error: "User not found");

        // Accounts the old code "disabled" carry a lock-out date far in the future; clear it so they can sign in.
        // Both changes go out in one UPDATE.
        user.IsEnabled = true;
        user.LockoutEnd = null;
        var audit = StageAudit(AdminAuditAction.UserEnabled, userId);
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            DiscardAudit(audit);
            return IdentityFailure<bool>(result);
        }

        LogUserEnabled(logger, ActingAdmin, userId);
        return ServiceResult<bool>.NoContent();
    }

    public async Task<ServiceResult<AuthResponseDto>> ImpersonateUserAsync(string userId, string adminId, CancellationToken cancellationToken = default)
    {
        var target = await userManager.FindByIdAsync(userId);
        if (target is null)
            return ServiceResult<AuthResponseDto>.NotFound(error: "User not found.");

        var targetRoles = await userManager.GetRolesAsync(target);
        if (!targetRoles.Contains(RoleNames.Student))
            return ServiceResult<AuthResponseDto>.BadRequest(error: ErrorMessagesRes.ImpersonationOnlyStudents);

        // The page flow refuses a disabled member too; a disabled account gets no session of any kind.
        if (!target.IsEnabled)
            return ServiceResult<AuthResponseDto>.BadRequest(error: ErrorMessagesRes.AccountDisabled);

        var (token, expiration) = await tokenService.GenerateImpersonationTokenAsync(target, adminId);

        // Saved before the token leaves the service, so a session is never handed out without its row.
        var audit = AdminAuditStaging.Stage(context, adminId, userId, AdminAuditAction.ImpersonationStarted, timeProvider.GetUtcNow());
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            AdminAuditStaging.Discard(context, audit);
            throw;
        }

        LogImpersonationStarted(logger, adminId, userId);

        return ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto
        {
            Success = true,
            Token = token,
            Expiration = expiration
        });
    }

    public async Task<ServiceResult<bool>> SetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return ServiceResult<bool>.NotFound(error: "User not found.");

        if (await userManager.IsInRoleAsync(user, RoleNames.Admin))
            return ServiceResult<bool>.BadRequest(error: ErrorMessagesRes.AdminPasswordNotChangeable);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        // The row (never the password) goes into the save that stores the new password hash.
        var audit = StageAudit(AdminAuditAction.PasswordSet, userId);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            DiscardAudit(audit);
            return ServiceResult<bool>.BadRequest(error: string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        // SEC-1: this rotated SecurityStamp — revoke outstanding refresh tokens explicitly
        // rather than waiting for RefreshAsync's stamp check to reject them one by one.
        await RefreshTokenRevocation.RevokeActiveTokensAsync(context, userId, cancellationToken);
        liveConnectionCloser.CloseAll(userId);

        LogPasswordSetByAdmin(logger, ActingAdmin, userId);
        await SendPasswordChangedNoticeAsync(user, cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    // The member is told in their own language, since the admin may read the site in another one. The password
    // is already changed here, so a notice that cannot be queued is logged (by id, never the address), not an error.
    private async Task SendPasswordChangedNoticeAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(user.Email))
            return;

        try
        {
            await emailService.SendPasswordChangedByAdminAsync(user.Email, user.PreferredLanguage, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPasswordChangedNoticeFailed(logger, ex, user.Id);
        }
    }

    // Ends every session of the user: the new security stamp makes the cookie and bearer-token checks reject
    // what is already signed in, and the refresh tokens can no longer renew it. The refresh tokens go first, so a
    // failed stamp update never leaves a token that could still renew a session. The stamp update saves every
    // pending change of the user too: userChange runs right before it, so that change and the stamp are one UPDATE.
    // Once the stamp is saved, the open chat, call and notification connections of the user are closed too.
    // An audited action is staged after the refresh tokens' own save, so its row joins the stamp update's save
    // (the one that stores the action) and a failed update leaves no row. A retry after a concurrency failure
    // saves the same row, so there is still exactly one.
    private async Task<IdentityResult> EndSessionsAsync(
        ApplicationUser user, CancellationToken cancellationToken, Action<ApplicationUser>? userChange = null,
        AdminAuditAction? auditedAction = null)
    {
        await RefreshTokenRevocation.RevokeActiveTokensAsync(context, user.Id, cancellationToken);

        var audit = auditedAction is { } action ? StageAudit(action, user.Id) : null;
        userChange?.Invoke(user);
        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (HasConcurrencyFailure(stampResult))
        {
            // The row changed since it was read (a sign-in, a second admin). The tracked user still carries the old
            // concurrency stamp, so take the stored values, apply the change again and try once more.
            await context.Entry(user).ReloadAsync(cancellationToken);
            userChange?.Invoke(user);
            stampResult = await userManager.UpdateSecurityStampAsync(user);
        }

        if (stampResult.Succeeded)
            liveConnectionCloser.CloseAll(user.Id);
        else
            DiscardAudit(audit);

        return stampResult;
    }

    // The audit row of this action, staged so the save that stores the action stores the row too.
    private AdminAuditEntry StageAudit(AdminAuditAction action, string targetUserId, string? detail = null)
        => AdminAuditStaging.Stage(context, ActingAdmin, targetUserId, action, timeProvider.GetUtcNow(), detail);

    // A failed action leaves no row, not even one that a later save of the same request could store.
    private void DiscardAudit(AdminAuditEntry? audit)
    {
        if (audit is not null)
            AdminAuditStaging.Discard(context, audit);
    }

    private static bool HasConcurrencyFailure(IdentityResult result)
    {
        var concurrencyFailureCode = new IdentityErrorDescriber().ConcurrencyFailure().Code;
        return result.Errors.Any(error => error.Code == concurrencyFailureCode);
    }

    // A concurrency failure means the account changed at the same time: 409 asks the admin to try again.
    private static ServiceResult<T> IdentityFailure<T>(IdentityResult result)
        => HasConcurrencyFailure(result)
            ? ServiceResult<T>.Conflict(error: "The account was changed at the same time. Please try again.")
            : ServiceResult<T>.BadRequest(error: string.Join(", ", result.Errors.Select(e => e.Description)));

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} assigned role {Role} to user {UserId}")]
    private static partial void LogRoleAssigned(ILogger logger, string adminId, string role, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} removed role {Role} from user {UserId}")]
    private static partial void LogRoleRemoved(ILogger logger, string adminId, string role, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} created role {Role}")]
    private static partial void LogRoleCreated(ILogger logger, string adminId, string role);

    [LoggerMessage(Level = LogLevel.Error, Message = "Admin {AdminId} deleting user {UserId} failed on a database constraint")]
    private static partial void LogDeleteUserConstraintFailed(ILogger logger, Exception exception, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "User {UserId} deleting their own account failed on a database constraint")]
    private static partial void LogOwnAccountErasureConstraintFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} deleted user {UserId} and associated chat/call history")]
    private static partial void LogUserDeleted(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} deleted their own account and associated chat/call history")]
    private static partial void LogAccountErasedByOwner(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The profile photo of deleted user {UserId} could not be removed from storage")]
    private static partial void LogAvatarFileNotDeleted(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A certificate PDF of deleted user {UserId} could not be removed from storage")]
    private static partial void LogCertificatePdfNotDeleted(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The password-changed notice for user {UserId} could not be sent")]
    private static partial void LogPasswordChangedNoticeFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} triggered forced password reset for user {UserId}.")]
    private static partial void LogForcedPasswordResetTriggered(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} disabled user {UserId}")]
    private static partial void LogUserDisabled(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} enabled user {UserId}")]
    private static partial void LogUserEnabled(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} started impersonating user {UserId}")]
    private static partial void LogImpersonationStarted(ILogger logger, string adminId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} set new password for user {UserId}")]
    private static partial void LogPasswordSetByAdmin(ILogger logger, string adminId, string userId);
}
