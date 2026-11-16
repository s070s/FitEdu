using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The member's own file name (a photo called after a person or a place) never reaches the storage: the service hands
/// over only the validated content type, and the storage names the file by a GUID.
/// </summary>
public class ProfileAvatarUploadTests
{
    private const string UserId = "member-1";

    [Fact]
    public async Task UploadAvatar_PassesOnlyTheContentTypeToTheStorage()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var userManager = IdentityMocks.MockUserManager();
        userManager.FindByIdAsync(UserId).Returns(new ApplicationUser { Id = UserId, FirstName = "Maria", LastName = "Papa" });
        userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns("avatars/0123456789abcdef0123456789abcdef.png");
        var service = new ProfileService(userManager, fileStorage, db, NullLogger<ProfileService>.Instance, TimeProvider.System);

        var result = await service.UploadAvatarAsync(UserId, new MemoryStream([1, 2, 3]), "image/png");

        await fileStorage.Received(1).SaveFileAsync(
            Arg.Any<Stream>(), "image/png", "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>());
        result.Value!.AvatarPath.ShouldBe("avatars/0123456789abcdef0123456789abcdef.png");
    }

    // P05-6: save the new file, update the user, then delete the old file. A failure before the update keeps the
    // old file and the old value; a failure of the delete is only logged.

    private const string OldPath = "avatars/old.png";
    private const string NewPath = "avatars/0123456789abcdef0123456789abcdef.png";

    private sealed record Arranged(ProfileService Service, IFileStorage Storage, UserManager<ApplicationUser> Users, ApplicationUser User, ApplicationDbContext Db);

    private static Arranged ArrangeAMemberWithAnOldAvatar(bool userUpdateSucceeds = true)
    {
        var db = DbContextFactory.CreateInMemory();
        var user = new ApplicationUser { Id = UserId, FirstName = "Maria", LastName = "Papa", AvatarPath = OldPath };
        var users = IdentityMocks.MockUserManager();
        users.FindByIdAsync(UserId).Returns(user);
        users.UpdateAsync(Arg.Any<ApplicationUser>())
            .Returns(userUpdateSucceeds ? IdentityResult.Success : IdentityResult.Failed(new IdentityError { Description = "db down" }));
        var storage = Substitute.For<IFileStorage>();
        storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(NewPath);
        var service = new ProfileService(users, storage, db, NullLogger<ProfileService>.Instance, TimeProvider.System);
        return new Arranged(service, storage, users, user, db);
    }

    [Fact]
    public async Task UploadAvatar_SavesTheNewFileThenUpdatesTheUserThenDeletesTheOldOne()
    {
        var arranged = ArrangeAMemberWithAnOldAvatar();
        await using var db = arranged.Db;

        var result = await arranged.Service.UploadAvatarAsync(UserId, new MemoryStream([1]), "image/png");

        result.IsSuccess.ShouldBeTrue();
        arranged.User.AvatarPath.ShouldBe(NewPath);
        Received.InOrder(() =>
        {
            arranged.Storage.SaveFileAsync(Arg.Any<Stream>(), "image/png", "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>());
            arranged.Users.UpdateAsync(arranged.User);
            arranged.Storage.DeleteFileAsync(OldPath, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task UploadAvatar_SaveFails_KeepsTheOldFileAndTheOldValue()
    {
        var arranged = ArrangeAMemberWithAnOldAvatar();
        await using var db = arranged.Db;
        arranged.Storage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));

        await Should.ThrowAsync<IOException>(
            () => arranged.Service.UploadAvatarAsync(UserId, new MemoryStream([1]), "image/png"));

        arranged.User.AvatarPath.ShouldBe(OldPath);
        await arranged.Storage.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await arranged.Users.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task UploadAvatar_UserUpdateFails_DeletesTheNewFileAndKeepsTheOldOne()
    {
        var arranged = ArrangeAMemberWithAnOldAvatar(userUpdateSucceeds: false);
        await using var db = arranged.Db;

        var result = await arranged.Service.UploadAvatarAsync(UserId, new MemoryStream([1]), "image/png");

        result.IsSuccess.ShouldBeFalse();
        arranged.User.AvatarPath.ShouldBe(OldPath);
        await arranged.Storage.Received(1).DeleteFileAsync(NewPath, Arg.Any<CancellationToken>());
        await arranged.Storage.DidNotReceive().DeleteFileAsync(OldPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAvatar_DeletingTheOldFileFails_StillSucceeds()
    {
        var arranged = ArrangeAMemberWithAnOldAvatar();
        await using var db = arranged.Db;
        arranged.Storage.DeleteFileAsync(OldPath, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("file in use"));

        var result = await arranged.Service.UploadAvatarAsync(UserId, new MemoryStream([1]), "image/png");

        result.IsSuccess.ShouldBeTrue();
        result.Value!.AvatarPath.ShouldBe(NewPath);
    }
}
