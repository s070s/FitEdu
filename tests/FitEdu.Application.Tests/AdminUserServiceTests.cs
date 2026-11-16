using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Application.Common;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// DeleteUserAsync tests run on SQLite (not InMemory) because the fix under test is
/// about real FK behavior: Restrict FKs from chat/call history and the
/// Certificate→Enrollment NoAction diamond.
/// </summary>
public class AdminUserServiceTests
{
    private static AdminUserService NewService(
        ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailService? emailService = null, ITokenService? tokenService = null,
        IFileStorage? fileStorage = null, ILogger<AdminUserService>? logger = null, string actingAdminId = "admin-actor",
        RoleManager<IdentityRole>? roleManager = null, ILiveConnectionCloser? liveConnectionCloser = null, TimeProvider? timeProvider = null)
    {
        roleManager ??= Substitute.For<RoleManager<IdentityRole>>(
            Substitute.For<IRoleStore<IdentityRole>>(), null, null, null, null);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(actingAdminId);

        return new AdminUserService(
            userManager,
            roleManager,
            tokenService ?? Substitute.For<ITokenService>(),
            logger ?? NullLogger<AdminUserService>.Instance,
            db,
            emailService ?? Substitute.For<IEmailService>(),
            fileStorage ?? Substitute.For<IFileStorage>(),
            liveConnectionCloser ?? Substitute.For<ILiveConnectionCloser>(),
            currentUser,
            timeProvider ?? TimeProvider.System,
            Options.Create(new AppOptions { BaseUrl = "https://site.test" }));
    }

    /// <summary>
    /// UserManager substitute that mirrors the real UserStore over the same scoped context:
    /// FindByIdAsync reads from the context, and DeleteAsync removes the user then calls
    /// SaveChangesAsync — flushing whatever the service staged in the change tracker,
    /// exactly like production where both share one ApplicationDbContext.
    /// </summary>
    private static UserManager<ApplicationUser> UserManagerOver(ApplicationDbContext db)
    {
        var userManager = IdentityMocks.MockUserManager();

        userManager.FindByIdAsync(Arg.Any<string>())
            .Returns(ci => db.Users.SingleOrDefaultAsync(u => u.Id == ci.Arg<string>()));

        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(false);

        userManager.DeleteAsync(Arg.Any<ApplicationUser>())
            .Returns(async ci =>
            {
                db.Users.Remove(ci.Arg<ApplicationUser>());
                await db.SaveChangesAsync();
                return IdentityResult.Success;
            });

        return userManager;
    }

    private static ApplicationUser NewUser(string id) => new()
    {
        Id = id,
        UserName = $"{id}@test.local",
        Email = $"{id}@test.local",
        FirstName = "First",
        LastName = "Last"
    };

    [Fact]
    public async Task DeleteUser_WithChatCallCertificateHistory_DeletesUserAndHistory()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var victim = NewUser("victim");
        var other = NewUser("other");
        db.Users.AddRange(victim, other);

        // Chat history: conversation between the two, messages in both directions.
        var conversation = new ChatConversation { CreatorId = victim.Id, ParticipantId = other.Id };
        db.ChatConversations.Add(conversation);
        db.ChatMessages.Add(new ChatMessage { ConversationId = conversation.Id, SenderId = victim.Id, Content = "hi" });
        db.ChatMessages.Add(new ChatMessage { ConversationId = conversation.Id, SenderId = other.Id, Content = "hey" });

        // Call history: one session initiated by the victim, one by the other user —
        // the victim is a participant in both.
        var victimCall = new CallSession { InitiatorId = victim.Id };
        var otherCall = new CallSession { InitiatorId = other.Id };
        db.CallSessions.AddRange(victimCall, otherCall);
        db.CallParticipants.Add(new CallParticipant { CallSessionId = victimCall.Id, UserId = victim.Id });
        db.CallParticipants.Add(new CallParticipant { CallSessionId = victimCall.Id, UserId = other.Id });
        db.CallParticipants.Add(new CallParticipant { CallSessionId = otherCall.Id, UserId = victim.Id });
        db.CallParticipants.Add(new CallParticipant { CallSessionId = otherCall.Id, UserId = other.Id });

        // Certificate→Enrollment NoAction diamond.
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);
        var enrollment = new Enrollment { Id = Guid.NewGuid(), UserId = victim.Id, CourseId = course.Id };
        db.Enrollments.Add(enrollment);
        db.Certificates.Add(new Certificate
        {
            Id = Guid.NewGuid(),
            UserId = victim.Id,
            EnrollmentId = enrollment.Id,
            CourseId = course.Id,
            RecipientName = "First Last",
            CourseTitleEn = "C"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await NewService(db, UserManagerOver(db)).DeleteUserAsync(victim.Id);

        result.IsSuccess.ShouldBeTrue();
        (await db.Users.AnyAsync(u => u.Id == victim.Id)).ShouldBeFalse();
        (await db.ChatConversations.CountAsync()).ShouldBe(0);
        (await db.ChatMessages.CountAsync()).ShouldBe(0);
        (await db.Certificates.CountAsync()).ShouldBe(0);
        (await db.Enrollments.CountAsync()).ShouldBe(0);
        // The victim's own session is gone with all its participant rows; the other
        // user's session survives with only the victim's participant row removed.
        (await db.CallSessions.SingleAsync()).Id.ShouldBe(otherCall.Id);
        (await db.CallParticipants.SingleAsync()).UserId.ShouldBe(other.Id);
        (await db.Users.AnyAsync(u => u.Id == other.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteUser_WithoutHistory_Succeeds()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("plain"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await NewService(db, UserManagerOver(db)).DeleteUserAsync("plain");

        result.IsSuccess.ShouldBeTrue();
        (await db.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteUser_ByAnAdmin_LogsTheAdminAndTheDeletedUser()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("victim"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var log = new RecordingLogger<AdminUserService>();

        await NewService(db, UserManagerOver(db), logger: log).DeleteUserAsync("victim");

        log.Information.ShouldContain(line => line.Contains("Admin admin-actor deleted user victim"));
    }

    [Fact]
    public async Task DeleteUser_OwnAccount_LogsASelfErasureNotAnAdminAction()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var log = new RecordingLogger<AdminUserService>();

        await NewService(db, UserManagerOver(db), logger: log, actingAdminId: "member").DeleteUserAsync("member");

        log.Information.ShouldContain(line => line.Contains("User member deleted their own account"));
        log.Information.ShouldNotContain(line => line.Contains("Admin member"));
    }

    [Fact]
    public async Task DeleteUser_OwnAccountFailsOnAConstraint_LogsAnOwnerNotAnAdmin()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.DeleteAsync(Arg.Any<ApplicationUser>()).ThrowsAsync(new DbUpdateException("FK violation"));
        var log = new RecordingLogger<AdminUserService>();

        await NewService(db, userManager, logger: log, actingAdminId: "member").DeleteUserAsync("member");

        log.Errors.ShouldContain(line => line.Contains("User member deleting their own account failed on a database constraint"));
        log.Errors.ShouldNotContain(line => line.Contains("Admin member"));
    }

    [Fact]
    public async Task DeleteUser_WithAnAvatar_DeletesTheFileOnceTheUserIsGone()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("with-avatar");
        member.AvatarPath = "avatars/me_0123.png";
        db.Users.Add(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fileStorage = Substitute.For<IFileStorage>();

        var result = await NewService(db, UserManagerOver(db), fileStorage: fileStorage).DeleteUserAsync("with-avatar");

        result.IsSuccess.ShouldBeTrue();
        await fileStorage.Received(1).DeleteFileAsync("avatars/me_0123.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUser_WithoutAnAvatar_TouchesNoFile()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("plain"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fileStorage = Substitute.For<IFileStorage>();

        await NewService(db, UserManagerOver(db), fileStorage: fileStorage).DeleteUserAsync("plain");

        await fileStorage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUser_FailedDelete_KeepsTheAvatarFile()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("blocked");
        member.AvatarPath = "avatars/me_0123.png";
        db.Users.Add(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.DeleteAsync(Arg.Any<ApplicationUser>()).ThrowsAsync(new DbUpdateException("FK violation"));
        var fileStorage = Substitute.For<IFileStorage>();

        await NewService(db, userManager, fileStorage: fileStorage).DeleteUserAsync("blocked");

        await fileStorage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUser_AvatarFileCannotBeRemoved_StillReportsTheDeletion()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("locked-file");
        member.AvatarPath = "avatars/me_0123.png";
        db.Users.Add(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("in use"));
        var log = new RecordingLogger<AdminUserService>();

        var result = await NewService(db, UserManagerOver(db), fileStorage: fileStorage, logger: log).DeleteUserAsync("locked-file");

        result.IsSuccess.ShouldBeTrue();
        log.Warnings.ShouldContain(line => line.Contains("locked-file"));
    }

    private static Certificate NewCertificate(ApplicationUser member, string? pdfPath, string? pdfPathEl)
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var enrollment = new Enrollment { Id = Guid.NewGuid(), UserId = member.Id, CourseId = course.Id };
        return new Certificate
        {
            Id = Guid.NewGuid(),
            UserId = member.Id,
            EnrollmentId = enrollment.Id,
            CourseId = course.Id,
            RecipientName = "First Last",
            CourseTitleEn = "C",
            PdfPath = pdfPath,
            PdfPathEl = pdfPathEl,
            Course = course,
            Enrollment = enrollment
        };
    }

    [Fact]
    public async Task DeleteUser_WithCertificatePdfs_DeletesBothFilesOfEachCertificateOnceTheUserIsGone()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("with-certificates");
        db.Users.Add(member);
        db.Certificates.Add(NewCertificate(member, "certificates/en-1.pdf", "certificates/el-1.pdf"));
        db.Certificates.Add(NewCertificate(member, "certificates/en-2.pdf", pdfPathEl: null));
        db.Certificates.Add(NewCertificate(member, pdfPath: null, pdfPathEl: null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fileStorage = Substitute.For<IFileStorage>();

        var result = await NewService(db, UserManagerOver(db), fileStorage: fileStorage).DeleteUserAsync("with-certificates");

        result.IsSuccess.ShouldBeTrue();
        await fileStorage.Received(1).DeleteFileAsync("certificates/en-1.pdf", Arg.Any<CancellationToken>());
        await fileStorage.Received(1).DeleteFileAsync("certificates/el-1.pdf", Arg.Any<CancellationToken>());
        await fileStorage.Received(1).DeleteFileAsync("certificates/en-2.pdf", Arg.Any<CancellationToken>());
        await fileStorage.Received(3).DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUser_FailedDelete_KeepsTheCertificatePdfs()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("blocked-certificates");
        db.Users.Add(member);
        db.Certificates.Add(NewCertificate(member, "certificates/en-1.pdf", "certificates/el-1.pdf"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.DeleteAsync(Arg.Any<ApplicationUser>()).ThrowsAsync(new DbUpdateException("FK violation"));
        var fileStorage = Substitute.For<IFileStorage>();

        await NewService(db, userManager, fileStorage: fileStorage).DeleteUserAsync("blocked-certificates");

        await fileStorage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUser_CertificatePdfCannotBeRemoved_StillDeletesTheOthersAndReportsTheDeletion()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("locked-certificate");
        db.Users.Add(member);
        db.Certificates.Add(NewCertificate(member, "certificates/en-1.pdf", "certificates/el-1.pdf"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.DeleteFileAsync("certificates/en-1.pdf", Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("in use"));
        var log = new RecordingLogger<AdminUserService>();

        var result = await NewService(db, UserManagerOver(db), fileStorage: fileStorage, logger: log).DeleteUserAsync("locked-certificate");

        result.IsSuccess.ShouldBeTrue();
        await fileStorage.Received(1).DeleteFileAsync("certificates/el-1.pdf", Arg.Any<CancellationToken>());
        log.Warnings.ShouldContain(line => line.Contains("locked-certificate"));
    }

    private static UserManager<ApplicationUser> UserManagerThatResetsPasswords(ApplicationDbContext db)
    {
        var userManager = UserManagerOver(db);
        userManager.GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>()).Returns("reset-token");
        userManager.ResetPasswordAsync(Arg.Any<ApplicationUser>(), "reset-token", Arg.Any<string>()).Returns(IdentityResult.Success);
        return userManager;
    }

    [Fact]
    public async Task SetPassword_SendsTheMemberANoticeInTheirOwnLanguage()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var greekMember = NewUser("greek-member");
        greekMember.PreferredLanguage = UserLanguage.Greek;
        db.Users.Add(greekMember);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var emailService = Substitute.For<IEmailService>();
        using var adminCulture = new CultureScope("en-GB");

        var result = await NewService(db, UserManagerThatResetsPasswords(db), emailService).SetPasswordAsync("greek-member", "New-Pass-1!");

        result.IsSuccess.ShouldBeTrue();
        await emailService.Received(1).SendPasswordChangedByAdminAsync(
            "greek-member@test.local", UserLanguage.Greek, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPassword_EndsTheMembersRefreshTokens()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = "member", TokenHash = "hash", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await NewService(db, UserManagerThatResetsPasswords(db)).SetPasswordAsync("member", "New-Pass-1!");

        (await db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task SetPassword_AdminAccount_IsRefusedAndNoNoticeIsSent()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("other-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerThatResetsPasswords(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        var emailService = Substitute.For<IEmailService>();

        var result = await NewService(db, userManager, emailService).SetPasswordAsync("other-admin", "New-Pass-1!");

        result.StatusCode.ShouldBe(400);
        result.ErrorMessage.ShouldBe(ErrorMessagesRes.AdminPasswordNotChangeable);
        await emailService.DidNotReceiveWithAnyArgs().SendPasswordChangedByAdminAsync(default!, default!);
    }

    [Fact]
    public async Task SetPassword_AdminAccount_IsRefusedInTheCallersLanguage()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("other-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerThatResetsPasswords(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        using var greek = new CultureScope("el");

        var result = await NewService(db, userManager).SetPasswordAsync("other-admin", "New-Pass-1!");

        result.ErrorMessage.ShouldBe(ErrorMessagesRes.AdminPasswordNotChangeable);
        result.ErrorMessage.ShouldNotBe("Admin account passwords cannot be changed from the user table.");
    }

    [Fact]
    public async Task SetPassword_RejectedPassword_SendsNoNotice()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerThatResetsPasswords(db);
        userManager.ResetPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Too common." }));
        var emailService = Substitute.For<IEmailService>();

        var result = await NewService(db, userManager, emailService).SetPasswordAsync("member", "Password1!");

        result.StatusCode.ShouldBe(400);
        await emailService.DidNotReceiveWithAnyArgs().SendPasswordChangedByAdminAsync(default!, default!);
    }

    [Fact]
    public async Task SetPassword_NoticeCannotBeQueued_StillSucceedsAndTheLogHasNoAddress()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var emailService = Substitute.For<IEmailService>();
        emailService.SendPasswordChangedByAdminAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The email queue is full."));
        var log = new RecordingLogger<AdminUserService>();

        var result = await NewService(db, UserManagerThatResetsPasswords(db), emailService, logger: log).SetPasswordAsync("member", "New-Pass-1!");

        result.IsSuccess.ShouldBeTrue();
        log.Warnings.ShouldHaveSingleItem().ShouldContain("member");
        log.Warnings.ShouldNotContain(line => line.Contains("member@test.local"));
    }

    [Fact]
    public async Task DeleteUser_Missing_ReturnsNotFound()
    {
        await using var db = DbContextFactory.CreateSqlite();

        var result = await NewService(db, UserManagerOver(db)).DeleteUserAsync("nope");

        result.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteUser_AdminAccount_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), "Admin").Returns(true);

        var result = await NewService(db, userManager).DeleteUserAsync("admin");

        result.StatusCode.ShouldBe(400);
        (await db.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ForcePasswordReset_SendsTheEmailInTheMemberLanguageNotTheAdminLanguage()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var greekMember = NewUser("greek-member");
        greekMember.PreferredLanguage = UserLanguage.Greek;
        db.Users.Add(greekMember);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var userManager = UserManagerOver(db);
        userManager.GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>()).Returns("reset-token");
        var emailService = Substitute.For<IEmailService>();
        using var adminCulture = new CultureScope("en-GB"); // the admin reads the site in English

        var result = await NewService(db, userManager, emailService)
            .ForcePasswordResetAsync("greek-member");

        result.IsSuccess.ShouldBeTrue();
        await emailService.Received(1).SendPasswordResetAsync(
            "greek-member@test.local", Arg.Any<string>(), UserLanguage.Greek, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForcePasswordReset_LinkStartsWithTheSiteAddressAndEscapesTheEmail()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("plus-member");
        member.Email = "a+b@test.local";
        db.Users.Add(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var userManager = UserManagerOver(db);
        userManager.GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>()).Returns("reset-token");
        var emailService = Substitute.For<IEmailService>();

        await NewService(db, userManager, emailService).ForcePasswordResetAsync("plus-member");

        await emailService.Received(1).SendPasswordResetAsync(
            "a+b@test.local", "https://site.test/reset-password?email=a%2Bb%40test.local&token=reset-token",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisableUser_TheActingAdminsOwnAccount_IsRefused()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("admin-actor"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);

        var result = await NewService(db, userManager).DisableUserAsync("admin-actor");

        result.StatusCode.ShouldBe(400);
        await userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task DisableUser_AdminAccount_IsRefused()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("other-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);

        var result = await NewService(db, userManager).DisableUserAsync("other-admin");

        result.StatusCode.ShouldBe(400);
        await userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task RemoveRole_AdminFromTheActingAdminThemselves_IsRefused()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("admin-actor"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        userManager.GetUsersInRoleAsync(RoleNames.Admin).Returns([NewUser("admin-actor"), NewUser("second-admin")]);

        var result = await NewService(db, userManager).RemoveRoleAsync("admin-actor", RoleNames.Admin);

        result.StatusCode.ShouldBe(400);
        await userManager.DidNotReceive().RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RemoveRole_AdminFromTheLastAdmin_IsRefused()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("only-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        userManager.GetUsersInRoleAsync(RoleNames.Admin).Returns([NewUser("only-admin")]);

        var result = await NewService(db, userManager).RemoveRoleAsync("only-admin", RoleNames.Admin);

        result.StatusCode.ShouldBe(400);
        await userManager.DidNotReceive().RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RemoveRole_AdminWhenTheOtherAdminIsDisabled_IsRefused()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("second-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        var disabledAdmin = NewUser("disabled-admin");
        disabledAdmin.IsEnabled = false;
        userManager.GetUsersInRoleAsync(RoleNames.Admin).Returns([NewUser("second-admin"), disabledAdmin]);

        var result = await NewService(db, userManager).RemoveRoleAsync("second-admin", RoleNames.Admin);

        result.StatusCode.ShouldBe(400);
        await userManager.DidNotReceive().RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RemoveRole_AdminWhenAnotherEnabledAdminRemains_IsAllowed()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("second-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        userManager.GetUsersInRoleAsync(RoleNames.Admin).Returns([NewUser("second-admin"), NewUser("third-admin")]);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(IdentityResult.Success);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        var result = await NewService(db, userManager).RemoveRoleAsync("second-admin", RoleNames.Admin);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveRole_TwoAdminsRemovedAtTheSameTime_LeavesOneAdmin()
    {
        await using var firstDb = DbContextFactory.CreateSqlite();
        await using var secondDb = DbContextFactory.CreateSqlite();
        var admins = new List<ApplicationUser> { NewUser("admin-a"), NewUser("admin-b") };
        var userManager = IdentityMocks.MockUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(call => admins.Single(a => a.Id == call.Arg<string>()));
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        userManager.GetUsersInRoleAsync(RoleNames.Admin).Returns(_ =>
        {
            lock (admins)
                return admins.ToList();
        });
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(async call =>
        {
            // Slow enough that an unguarded second request reads the list before this one has changed it.
            await Task.Delay(150);
            lock (admins)
                admins.Remove(call.Arg<ApplicationUser>());
            return IdentityResult.Success;
        });
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        var results = await Task.WhenAll(
            NewService(firstDb, userManager).RemoveRoleAsync("admin-a", RoleNames.Admin),
            NewService(secondDb, userManager).RemoveRoleAsync("admin-b", RoleNames.Admin));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        admins.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveRole_StudentFromTheActingAdmin_IsStillAllowed()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("admin-actor"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(IdentityResult.Success);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);

        var result = await NewService(db, userManager).RemoveRoleAsync("admin-actor", RoleNames.Student);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Impersonate_DisabledStudent_IsRefusedAndNoTokenIsIssued()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var student = NewUser("disabled-student");
        student.IsEnabled = false;
        db.Users.Add(student);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns([RoleNames.Student]);
        var tokenService = Substitute.For<ITokenService>();

        var result = await NewService(db, userManager, tokenService: tokenService).ImpersonateUserAsync("disabled-student", "admin-actor");

        result.StatusCode.ShouldBe(400);
        await tokenService.DidNotReceive().GenerateImpersonationTokenAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Impersonate_EnabledStudent_IssuesATokenForThatStudent()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("student"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns([RoleNames.Student]);
        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateImpersonationTokenAsync(Arg.Any<ApplicationUser>(), "admin-actor")
            .Returns(("impersonation-token", DateTime.UtcNow.AddHours(1)));

        var result = await NewService(db, userManager, tokenService: tokenService).ImpersonateUserAsync("student", "admin-actor");

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Token.ShouldBe("impersonation-token");
    }

    [Fact]
    public async Task Impersonate_AccountWithoutTheStudentRole_IsRefused()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("coach"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns([RoleNames.Admin]);

        var result = await NewService(db, userManager).ImpersonateUserAsync("coach", "admin-actor");

        result.StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task DeleteUser_ConstraintViolation_ReturnsConflict()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("blocked"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var userManager = UserManagerOver(db);
        userManager.DeleteAsync(Arg.Any<ApplicationUser>())
            .ThrowsAsync(new DbUpdateException("FK violation"));

        var result = await NewService(db, userManager).DeleteUserAsync("blocked");

        result.StatusCode.ShouldBe(409);
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
    }

    private static IdentityResult ConcurrencyFailure() => IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());

    /// <summary>
    /// Like the real store, a security-stamp update saves everything the service changed on the user
    /// (the substitute shares the service's context), so the test sees what one UPDATE would store.
    /// </summary>
    private static UserManager<ApplicationUser> UserManagerThatEndsSessions(ApplicationDbContext db)
    {
        var userManager = UserManagerOver(db);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });
        return userManager;
    }

    private static async Task<ApplicationDbContext> SqliteWithAMemberAndAToken()
    {
        var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = "member", TokenHash = "hash", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task DisableUser_SavesTheFlagWithTheStampInOneUpdate_AndEndsTheRefreshTokens()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);

        var result = await NewService(db, userManager).DisableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        await userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
        await userManager.Received(1).UpdateSecurityStampAsync(Arg.Any<ApplicationUser>());
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync()).IsEnabled.ShouldBeFalse();
        (await db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DisableUser_EndsTheRefreshTokensBeforeTheStampIsRotated()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        var tokensEndedWhenTheStampWasRotated = false;
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            tokensEndedWhenTheStampWasRotated = await db.RefreshTokens.AsNoTracking().AllAsync(t => t.RevokedAt != null);
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        await NewService(db, userManager).DisableUserAsync("member");

        tokensEndedWhenTheStampWasRotated.ShouldBeTrue();
    }

    [Fact]
    public async Task DisableUser_ARowChangedSinceItWasRead_IsReloadedAndTheFlagStillSaved()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        var attempts = 0;
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            attempts++;
            if (attempts > 1)
            {
                await db.SaveChangesAsync();
                return IdentityResult.Success;
            }

            // Someone else wrote the row after the service read it: the stored stamp no longer matches.
            await db.Database.ExecuteSqlRawAsync("UPDATE AspNetUsers SET ConcurrencyStamp = 'changed-elsewhere' WHERE Id = 'member'");
            return ConcurrencyFailure();
        });

        var result = await NewService(db, userManager).DisableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        attempts.ShouldBe(2);
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync()).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task DisableUser_ARowThatKeepsChanging_ReturnsConflict()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());

        var result = await NewService(db, userManager).DisableUserAsync("member");

        result.StatusCode.ShouldBe(409);
        await userManager.Received(2).UpdateSecurityStampAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task EnableUser_SetsTheFlagAndClearsTheLockoutInOneUpdate()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("member");
        member.IsEnabled = false;
        member.LockoutEnd = DateTimeOffset.MaxValue;
        db.Users.Add(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager).EnableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        await userManager.Received(1).UpdateAsync(Arg.Any<ApplicationUser>());
        await userManager.DidNotReceive().SetLockoutEndDateAsync(Arg.Any<ApplicationUser>(), Arg.Any<DateTimeOffset?>());
        db.ChangeTracker.Clear();
        var stored = await db.Users.SingleAsync();
        stored.IsEnabled.ShouldBeTrue();
        stored.LockoutEnd.ShouldBeNull();
    }

    [Fact]
    public async Task EnableUser_ARowChangedSinceItWasRead_ReturnsConflict()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());

        var result = await NewService(db, userManager).EnableUserAsync("member");

        result.StatusCode.ShouldBe(409);
    }

    [Fact]
    public async Task AssignRole_EndsTheRefreshTokensAndRotatesTheStampOnce()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);
        userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(IdentityResult.Success);
        var roleManager = Substitute.For<RoleManager<IdentityRole>>(Substitute.For<IRoleStore<IdentityRole>>(), null, null, null, null);
        roleManager.RoleExistsAsync(RoleNames.Admin).Returns(true);

        var result = await NewService(db, userManager, roleManager: roleManager).AssignRoleAsync("member", RoleNames.Admin);

        result.IsSuccess.ShouldBeTrue();
        await userManager.Received(1).UpdateSecurityStampAsync(Arg.Any<ApplicationUser>());
        (await db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task RemoveRole_EndsTheRefreshTokensAndRotatesTheStampOnce()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(IdentityResult.Success);

        var result = await NewService(db, userManager).RemoveRoleAsync("member", RoleNames.Student);

        result.IsSuccess.ShouldBeTrue();
        await userManager.Received(1).UpdateSecurityStampAsync(Arg.Any<ApplicationUser>());
        (await db.RefreshTokens.SingleAsync()).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DisableUser_ClosesTheMembersLiveConnections()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, UserManagerThatEndsSessions(db), liveConnectionCloser: closer).DisableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        closer.Received(1).CloseAll("member");
    }

    [Fact]
    public async Task DisableUser_WhenTheSessionsCannotBeEnded_ClosesNothing()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, userManager, liveConnectionCloser: closer).DisableUserAsync("member");

        result.IsSuccess.ShouldBeFalse();
        closer.DidNotReceiveWithAnyArgs().CloseAll(default!);
    }

    [Fact]
    public async Task AssignRole_ClosesTheMembersLiveConnections()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);
        userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(IdentityResult.Success);
        var roleManager = Substitute.For<RoleManager<IdentityRole>>(Substitute.For<IRoleStore<IdentityRole>>(), null, null, null, null);
        roleManager.RoleExistsAsync(RoleNames.Admin).Returns(true);
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, userManager, roleManager: roleManager, liveConnectionCloser: closer)
            .AssignRoleAsync("member", RoleNames.Admin);

        result.IsSuccess.ShouldBeTrue();
        closer.Received(1).CloseAll("member");
    }

    [Fact]
    public async Task RemoveRole_ClosesTheMembersLiveConnections()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(IdentityResult.Success);
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, userManager, liveConnectionCloser: closer).RemoveRoleAsync("member", RoleNames.Student);

        result.IsSuccess.ShouldBeTrue();
        closer.Received(1).CloseAll("member");
    }

    [Fact]
    public async Task SetPassword_ClosesTheMembersLiveConnections()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, UserManagerThatResetsPasswords(db), liveConnectionCloser: closer).SetPasswordAsync("member", "New-Pass-1!");

        result.IsSuccess.ShouldBeTrue();
        closer.Received(1).CloseAll("member");
    }

    [Fact]
    public async Task SetPassword_RejectedPassword_ClosesNothing()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerThatResetsPasswords(db);
        userManager.ResetPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Too common." }));
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, userManager, liveConnectionCloser: closer).SetPasswordAsync("member", "Password1!");

        result.StatusCode.ShouldBe(400);
        closer.DidNotReceiveWithAnyArgs().CloseAll(default!);
    }

    [Fact]
    public async Task AssignRole_WhenTheSessionsCannotBeEnded_SaysTheRoleWasChangedAndClosesNothing()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(IdentityResult.Success);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Failed(new IdentityError { Description = "Store failed." }));
        var roleManager = Substitute.For<RoleManager<IdentityRole>>(Substitute.For<IRoleStore<IdentityRole>>(), null, null, null, null);
        roleManager.RoleExistsAsync(RoleNames.Admin).Returns(true);
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, userManager, roleManager: roleManager, liveConnectionCloser: closer)
            .AssignRoleAsync("member", RoleNames.Admin);

        result.StatusCode.ShouldBe(409);
        result.ErrorMessage.ShouldBe(ErrorMessagesRes.RoleChangedSessionsNotEnded);
        closer.DidNotReceiveWithAnyArgs().CloseAll(default!);
    }

    [Fact]
    public async Task RemoveRole_WhenTheSessionsCannotBeEnded_SaysTheRoleWasChangedAndClosesNothing()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(IdentityResult.Success);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());
        var closer = Substitute.For<ILiveConnectionCloser>();

        var result = await NewService(db, userManager, liveConnectionCloser: closer).RemoveRoleAsync("member", RoleNames.Student);

        result.StatusCode.ShouldBe(409);
        result.ErrorMessage.ShouldBe(ErrorMessagesRes.RoleChangedSessionsNotEnded);
        closer.DidNotReceiveWithAnyArgs().CloseAll(default!);
    }

    // ---- Admin audit log: one row per action, in the action's own save ---------------------------------------

    private static readonly DateTimeOffset AuditClock = new(2026, 11, 16, 8, 30, 0, TimeSpan.Zero);

    // Rows the change tracker still has to insert: what the next SaveChanges would store.
    private static int PendingAuditRows(ApplicationDbContext db) =>
        db.ChangeTracker.Entries<AdminAuditEntry>().Count(entry => entry.State == EntityState.Added);

    private static Task<List<AdminAuditEntry>> StoredAuditRows(ApplicationDbContext db) =>
        db.AdminAuditEntries.AsNoTracking().ToListAsync();

    // A failed action must leave nothing behind, not even a row a later save of the same request could store.
    private static async Task ShouldHaveWrittenNoAuditRow(ApplicationDbContext db)
    {
        PendingAuditRows(db).ShouldBe(0);
        await db.SaveChangesAsync();
        (await StoredAuditRows(db)).ShouldBeEmpty();
    }

    private static RoleManager<IdentityRole> RoleManagerWithRole(string roleName)
    {
        var roleManager = Substitute.For<RoleManager<IdentityRole>>(Substitute.For<IRoleStore<IdentityRole>>(), null, null, null, null);
        roleManager.RoleExistsAsync(roleName).Returns(true);
        return roleManager;
    }

    private static void ShouldBeTheRow(
        AdminAuditEntry row, string targetUserId, AdminAuditAction action, string? detail = null, string actorUserId = "admin-actor")
    {
        row.ActorUserId.ShouldBe(actorUserId);
        row.TargetUserId.ShouldBe(targetUserId);
        row.Action.ShouldBe(action);
        row.Detail.ShouldBe(detail);
        row.OccurredAt.ShouldBe(AuditClock);
    }

    [Fact]
    public async Task DisableUser_WritesOneAuditRowInTheSaveThatDisablesTheMember()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        var pendingWhenTheStampWasSaved = -1;
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            pendingWhenTheStampWasSaved = PendingAuditRows(db);
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager, timeProvider: new FixedTimeProvider(AuditClock)).DisableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        pendingWhenTheStampWasSaved.ShouldBe(1);
        ShouldBeTheRow((await StoredAuditRows(db)).ShouldHaveSingleItem(), "member", AdminAuditAction.UserDisabled);
    }

    [Fact]
    public async Task DisableUser_ARowChangedSinceItWasRead_StillWritesExactlyOneAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        var attempts = 0;
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            attempts++;
            if (attempts > 1)
            {
                await db.SaveChangesAsync();
                return IdentityResult.Success;
            }

            await db.Database.ExecuteSqlRawAsync("UPDATE AspNetUsers SET ConcurrencyStamp = 'changed-elsewhere' WHERE Id = 'member'");
            try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { }
            return ConcurrencyFailure();
        });

        var result = await NewService(db, userManager).DisableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        (await StoredAuditRows(db)).ShouldHaveSingleItem().Action.ShouldBe(AdminAuditAction.UserDisabled);
    }

    [Fact]
    public async Task DisableUser_ARowThatKeepsChanging_WritesNoAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());

        var result = await NewService(db, userManager).DisableUserAsync("member");

        result.StatusCode.ShouldBe(409);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task DisableUser_TheStoreRefusesTheUpdate_WritesNoAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Failed(new IdentityError { Description = "Store failed." }));

        var result = await NewService(db, userManager).DisableUserAsync("member");

        result.StatusCode.ShouldBe(400);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task DisableUser_RefusedOrMissing_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.AddRange(NewUser("admin-actor"), NewUser("other-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Is<ApplicationUser>(user => user.Id == "other-admin"), RoleNames.Admin).Returns(true);
        var service = NewService(db, userManager);

        (await service.DisableUserAsync("admin-actor")).StatusCode.ShouldBe(400);
        (await service.DisableUserAsync("other-admin")).StatusCode.ShouldBe(400);
        (await service.DisableUserAsync("nobody")).StatusCode.ShouldBe(404);

        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task EnableUser_WritesOneAuditRowInTheSaveThatEnablesTheMember()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("member");
        member.IsEnabled = false;
        db.Users.Add(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        var pendingWhenTheUserWasSaved = -1;
        userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(async _ =>
        {
            pendingWhenTheUserWasSaved = PendingAuditRows(db);
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager, timeProvider: new FixedTimeProvider(AuditClock)).EnableUserAsync("member");

        result.IsSuccess.ShouldBeTrue();
        pendingWhenTheUserWasSaved.ShouldBe(1);
        ShouldBeTheRow((await StoredAuditRows(db)).ShouldHaveSingleItem(), "member", AdminAuditAction.UserEnabled);
    }

    [Fact]
    public async Task EnableUser_TheUpdateFails_WritesNoAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());

        var result = await NewService(db, userManager).EnableUserAsync("member");

        result.StatusCode.ShouldBe(409);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task EnableUser_MissingMember_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();

        var result = await NewService(db, UserManagerOver(db)).EnableUserAsync("nobody");

        result.StatusCode.ShouldBe(404);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task AssignRole_WritesOneAuditRowWithTheRoleNameInTheSaveThatAddsTheRole()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);
        var pendingWhenTheRoleWasSaved = -1;
        userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(async _ =>
        {
            pendingWhenTheRoleWasSaved = PendingAuditRows(db);
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager, roleManager: RoleManagerWithRole(RoleNames.Admin), timeProvider: new FixedTimeProvider(AuditClock))
            .AssignRoleAsync("member", RoleNames.Admin);

        result.IsSuccess.ShouldBeTrue();
        pendingWhenTheRoleWasSaved.ShouldBe(1);
        ShouldBeTheRow((await StoredAuditRows(db)).ShouldHaveSingleItem(), "member", AdminAuditAction.RoleAdded, RoleNames.Admin);
    }

    [Fact]
    public async Task AssignRole_TheStoreRefusesTheRole_WritesNoAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(IdentityResult.Failed(new IdentityError { Description = "Store failed." }));

        var result = await NewService(db, userManager, roleManager: RoleManagerWithRole(RoleNames.Admin)).AssignRoleAsync("member", RoleNames.Admin);

        result.StatusCode.ShouldBe(400);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task AssignRole_UnknownRoleOrRoleAlreadyHeldOrMissingMember_WritesNoAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        var roleManager = RoleManagerWithRole(RoleNames.Student);
        var service = NewService(db, userManager, roleManager: roleManager);

        (await service.AssignRoleAsync("member", "Nonexistent")).StatusCode.ShouldBe(400);
        (await service.AssignRoleAsync("member", RoleNames.Student)).StatusCode.ShouldBe(400);
        (await service.AssignRoleAsync("nobody", RoleNames.Student)).StatusCode.ShouldBe(404);

        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task AssignRole_WhenTheSessionsCannotBeEnded_KeepsTheRowOfTheRoleThatWasSaved()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(async _ =>
        {
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());

        var result = await NewService(db, userManager, roleManager: RoleManagerWithRole(RoleNames.Admin)).AssignRoleAsync("member", RoleNames.Admin);

        result.StatusCode.ShouldBe(409);
        (await StoredAuditRows(db)).ShouldHaveSingleItem().Action.ShouldBe(AdminAuditAction.RoleAdded);
    }

    [Fact]
    public async Task RemoveRole_WritesOneAuditRowWithTheRoleNameInTheSaveThatRemovesTheRole()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerThatEndsSessions(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        var pendingWhenTheRoleWasSaved = -1;
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(async _ =>
        {
            pendingWhenTheRoleWasSaved = PendingAuditRows(db);
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager, timeProvider: new FixedTimeProvider(AuditClock)).RemoveRoleAsync("member", RoleNames.Student);

        result.IsSuccess.ShouldBeTrue();
        pendingWhenTheRoleWasSaved.ShouldBe(1);
        ShouldBeTheRow((await StoredAuditRows(db)).ShouldHaveSingleItem(), "member", AdminAuditAction.RoleRemoved, RoleNames.Student);
    }

    [Fact]
    public async Task RemoveRole_TheStoreRefusesTheRemoval_WritesNoAuditRow()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(IdentityResult.Failed(new IdentityError { Description = "Store failed." }));

        var result = await NewService(db, userManager).RemoveRoleAsync("member", RoleNames.Student);

        result.StatusCode.ShouldBe(400);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task RemoveRole_AdminRoleRefusedOrRoleNotHeldOrMissingMember_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.AddRange(NewUser("admin-actor"), NewUser("only-admin"), NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Is<ApplicationUser>(user => user.Id != "member"), RoleNames.Admin).Returns(true);
        userManager.GetUsersInRoleAsync(RoleNames.Admin).Returns([NewUser("only-admin")]);
        var service = NewService(db, userManager);

        (await service.RemoveRoleAsync("admin-actor", RoleNames.Admin)).StatusCode.ShouldBe(400);
        (await service.RemoveRoleAsync("only-admin", RoleNames.Admin)).StatusCode.ShouldBe(400);
        (await service.RemoveRoleAsync("member", RoleNames.Admin)).StatusCode.ShouldBe(400);
        (await service.RemoveRoleAsync("nobody", RoleNames.Admin)).StatusCode.ShouldBe(404);

        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task RemoveRole_WhenTheSessionsCannotBeEnded_KeepsTheRowOfTheRoleThatWasRemoved()
    {
        await using var db = await SqliteWithAMemberAndAToken();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(true);
        userManager.RemoveFromRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Student).Returns(async _ =>
        {
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });
        userManager.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(ConcurrencyFailure());

        var result = await NewService(db, userManager).RemoveRoleAsync("member", RoleNames.Student);

        result.StatusCode.ShouldBe(409);
        (await StoredAuditRows(db)).ShouldHaveSingleItem().Action.ShouldBe(AdminAuditAction.RoleRemoved);
    }

    [Fact]
    public async Task SetPassword_WritesOneAuditRowWithoutThePasswordInTheSaveThatChangesIt()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>()).Returns("reset-token");
        var pendingWhenThePasswordWasSaved = -1;
        userManager.ResetPasswordAsync(Arg.Any<ApplicationUser>(), "reset-token", Arg.Any<string>()).Returns(async _ =>
        {
            pendingWhenThePasswordWasSaved = PendingAuditRows(db);
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager, timeProvider: new FixedTimeProvider(AuditClock)).SetPasswordAsync("member", "New-Pass-1!");

        result.IsSuccess.ShouldBeTrue();
        pendingWhenThePasswordWasSaved.ShouldBe(1);
        var row = (await StoredAuditRows(db)).ShouldHaveSingleItem();
        ShouldBeTheRow(row, "member", AdminAuditAction.PasswordSet);
        new[] { row.ActorUserId, row.TargetUserId, row.Detail }.ShouldAllBe(text => text == null || !text.Contains("New-Pass-1!"));
    }

    [Fact]
    public async Task SetPassword_RejectedPassword_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("member"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerThatResetsPasswords(db);
        userManager.ResetPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Too common." }));

        var result = await NewService(db, userManager).SetPasswordAsync("member", "Password1!");

        result.StatusCode.ShouldBe(400);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task SetPassword_AdminAccountOrMissingMember_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("other-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerThatResetsPasswords(db);
        userManager.IsInRoleAsync(Arg.Any<ApplicationUser>(), RoleNames.Admin).Returns(true);
        var service = NewService(db, userManager);

        (await service.SetPasswordAsync("other-admin", "New-Pass-1!")).StatusCode.ShouldBe(400);
        (await service.SetPasswordAsync("nobody", "New-Pass-1!")).StatusCode.ShouldBe(404);

        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task DeleteUser_WritesOneAuditRowInTheSaveThatDeletesTheAccount_AndTheRowOutlivesIt()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("victim"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        var pendingWhenTheAccountWasDeleted = -1;
        userManager.DeleteAsync(Arg.Any<ApplicationUser>()).Returns(async call =>
        {
            pendingWhenTheAccountWasDeleted = PendingAuditRows(db);
            db.Users.Remove(call.Arg<ApplicationUser>());
            await db.SaveChangesAsync();
            return IdentityResult.Success;
        });

        var result = await NewService(db, userManager, timeProvider: new FixedTimeProvider(AuditClock)).DeleteUserAsync("victim");

        result.IsSuccess.ShouldBeTrue();
        pendingWhenTheAccountWasDeleted.ShouldBe(1);
        (await db.Users.AnyAsync(user => user.Id == "victim")).ShouldBeFalse();
        ShouldBeTheRow((await StoredAuditRows(db)).ShouldHaveSingleItem(), "victim", AdminAuditAction.AccountDeleted);
    }

    [Fact]
    public async Task DeleteUser_TheDeletionFails_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("victim"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.DeleteAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Failed(new IdentityError { Description = "Store failed." }));

        var result = await NewService(db, userManager).DeleteUserAsync("victim");

        result.StatusCode.ShouldBe(400);
        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task DeleteUser_AConstraintBlocksTheDeletion_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("blocked"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.DeleteAsync(Arg.Any<ApplicationUser>()).ThrowsAsync(new DbUpdateException("FK violation"));

        var result = await NewService(db, userManager).DeleteUserAsync("blocked");

        result.StatusCode.ShouldBe(409);
        PendingAuditRows(db).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteUser_OwnAccountAdminAccountOrMissingMember_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.AddRange(NewUser("admin-actor"), NewUser("other-admin"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.IsInRoleAsync(Arg.Is<ApplicationUser>(user => user.Id == "other-admin"), RoleNames.Admin).Returns(true);

        // A member erasing their own account (ProfileController) is not an admin action.
        (await NewService(db, userManager, actingAdminId: "admin-actor").DeleteUserAsync("admin-actor")).IsSuccess.ShouldBeTrue();
        (await NewService(db, userManager).DeleteUserAsync("other-admin")).StatusCode.ShouldBe(400);
        (await NewService(db, userManager).DeleteUserAsync("nobody")).StatusCode.ShouldBe(404);

        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task Impersonate_WritesOneAuditRowBeforeTheTokenIsHandedOut()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.Add(NewUser("student"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns([RoleNames.Student]);
        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateImpersonationTokenAsync(Arg.Any<ApplicationUser>(), "admin-actor")
            .Returns(("impersonation-token", DateTime.UtcNow.AddHours(1)));

        var result = await NewService(db, userManager, tokenService: tokenService, timeProvider: new FixedTimeProvider(AuditClock))
            .ImpersonateUserAsync("student", "admin-actor");

        result.IsSuccess.ShouldBeTrue();
        PendingAuditRows(db).ShouldBe(0);
        var row = (await StoredAuditRows(db)).ShouldHaveSingleItem();
        ShouldBeTheRow(row, "student", AdminAuditAction.ImpersonationStarted);
        new[] { row.ActorUserId, row.TargetUserId, row.Detail }.ShouldAllBe(text => text == null || !text.Contains("impersonation-token"));
    }

    [Fact]
    public async Task Impersonate_RefusedOrMissingOrNoTokenIssued_WritesNoAuditRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var disabled = NewUser("disabled-student");
        disabled.IsEnabled = false;
        db.Users.AddRange(disabled, NewUser("coach"), NewUser("student"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GetRolesAsync(Arg.Is<ApplicationUser>(user => user.Id == "coach")).Returns([RoleNames.Admin]);
        userManager.GetRolesAsync(Arg.Is<ApplicationUser>(user => user.Id != "coach")).Returns([RoleNames.Student]);
        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateImpersonationTokenAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("No signing key."));
        var service = NewService(db, userManager, tokenService: tokenService);

        (await service.ImpersonateUserAsync("nobody", "admin-actor")).StatusCode.ShouldBe(404);
        (await service.ImpersonateUserAsync("coach", "admin-actor")).StatusCode.ShouldBe(400);
        (await service.ImpersonateUserAsync("disabled-student", "admin-actor")).StatusCode.ShouldBe(400);
        await Should.ThrowAsync<InvalidOperationException>(() => service.ImpersonateUserAsync("student", "admin-actor"));

        await ShouldHaveWrittenNoAuditRow(db);
    }

    [Fact]
    public async Task Impersonate_TheSaveThrows_LeavesNoPendingAuditRow()
    {
        var store = new FailingSaveInterceptor();
        await using var db = DbContextFactory.CreateInMemory(store);
        db.Users.Add(NewUser("student"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var userManager = UserManagerOver(db);
        userManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns([RoleNames.Student]);
        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateImpersonationTokenAsync(Arg.Any<ApplicationUser>(), "admin-actor")
            .Returns(("impersonation-token", DateTime.UtcNow.AddHours(1)));
        store.FailSaves = true;

        await Should.ThrowAsync<DbUpdateException>(() =>
            NewService(db, userManager, tokenService: tokenService).ImpersonateUserAsync("student", "admin-actor"));

        PendingAuditRows(db).ShouldBe(0);
    }
}
