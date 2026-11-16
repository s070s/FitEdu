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
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
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
        IFileStorage? fileStorage = null, ILogger<AdminUserService>? logger = null, string actingAdminId = "admin-actor")
    {
        var roleManager = Substitute.For<RoleManager<IdentityRole>>(
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
            currentUser,
            TimeProvider.System,
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
        await emailService.DidNotReceiveWithAnyArgs().SendPasswordChangedByAdminAsync(default!, default!);
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
}
