using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The member's own file name (a photo called after a person or a place) must not reach the stored path or the log
/// line the storage writes for it, so the avatar is saved under a fixed name that keeps only the extension.
/// </summary>
public class ProfileAvatarUploadTests
{
    private const string UserId = "member-1";

    [Fact]
    public async Task UploadAvatar_SavesUnderAFixedNameWithTheOriginalExtension()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var userManager = IdentityMocks.MockUserManager();
        userManager.FindByIdAsync(UserId).Returns(new ApplicationUser { Id = UserId, FirstName = "Maria", LastName = "Papa" });
        userManager.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.SaveFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(call => $"avatars/{Path.GetFileNameWithoutExtension(call.ArgAt<string>(1))}_0123{Path.GetExtension(call.ArgAt<string>(1))}");
        var service = new ProfileService(userManager, fileStorage, db, NullLogger<ProfileService>.Instance, TimeProvider.System);

        var result = await service.UploadAvatarAsync(UserId, new MemoryStream([1, 2, 3]), "Maria Papa passport photo.PNG");

        await fileStorage.Received(1).SaveFileAsync(
            Arg.Any<Stream>(), "avatar.PNG", "avatars", Arg.Any<long?>(), Arg.Any<CancellationToken>());
        result.Value!.AvatarPath.ShouldStartWith("avatars/avatar_");
        result.Value.AvatarPath.ShouldNotContain("Maria");
    }
}
