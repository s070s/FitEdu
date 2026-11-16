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
}
