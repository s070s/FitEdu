using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.Common;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Hubs;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// An open hub connection outlives the sign-in that created it, so every hub call re-checks
/// that the account is still enabled and the session's security stamp is still current.
/// </summary>
public class ActiveSessionHubFilterTests
{
    private const string UserId = "user-1";
    private const string CurrentStamp = "stamp-current";

    private sealed class StubHub : Hub
    {
        public static Task Ping() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public required ActiveSessionHubFilter Filter;
        public required HubInvocationContext Invocation;
        public required HubCallerContext Context;
        public required Func<HubInvocationContext, ValueTask<object?>> Next;
        public int NextCalls;
    }

    private static Harness Build(ApplicationUser? storedUser, string? sessionStamp)
    {
        var userManager = IdentityMocks.MockUserManager();
        userManager.FindByIdAsync(UserId).Returns(storedUser);

        var claims = new List<Claim>();
        if (sessionStamp is not null)
            claims.Add(new Claim(UserClaimsBuilder.SecurityStampClaim, sessionStamp));

        var context = Substitute.For<HubCallerContext>();
        context.UserIdentifier.Returns(UserId);
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        var hub = new StubHub { Context = context };
        var invocation = new HubInvocationContext(
            context,
            Substitute.For<IServiceProvider>(),
            hub,
            typeof(StubHub).GetMethod(nameof(StubHub.Ping))!,
            []);

        var harness = new Harness
        {
            Filter = new ActiveSessionHubFilter(userManager, NullLogger<ActiveSessionHubFilter>.Instance),
            Invocation = invocation,
            Context = context,
            Next = null!
        };
        harness.Next = _ =>
        {
            harness.NextCalls++;
            return new ValueTask<object?>("done");
        };
        return harness;
    }

    private static ApplicationUser StoredUser(bool isEnabled = true, string stamp = CurrentStamp)
        => new() { Id = UserId, FirstName = "F", LastName = "L", IsEnabled = isEnabled, SecurityStamp = stamp };

    [Fact]
    public async Task EnabledUserWithCurrentStamp_PassesThrough()
    {
        var h = Build(StoredUser(), CurrentStamp);

        var result = await h.Filter.InvokeMethodAsync(h.Invocation, h.Next);

        result.ShouldBe("done");
        h.NextCalls.ShouldBe(1);
        h.Context.DidNotReceive().Abort();
    }

    [Fact]
    public async Task DisabledUser_AbortsTheConnection_AndRefusesTheCall()
    {
        var h = Build(StoredUser(isEnabled: false), CurrentStamp);

        var refusal = await Should.ThrowAsync<HubException>(async () => await h.Filter.InvokeMethodAsync(h.Invocation, h.Next));

        refusal.Message.ShouldBe(ErrorMessagesRes.AccountDisabled);

        h.Context.Received(1).Abort();
        h.NextCalls.ShouldBe(0);
    }

    [Fact]
    public async Task StaleStamp_AbortsTheConnection_AndRefusesTheCall()
    {
        var h = Build(StoredUser(), "stamp-from-before-the-role-change");

        var refusal = await Should.ThrowAsync<HubException>(async () => await h.Filter.InvokeMethodAsync(h.Invocation, h.Next));

        refusal.Message.ShouldBe(ErrorMessagesRes.NotAuthenticated);

        h.Context.Received(1).Abort();
        h.NextCalls.ShouldBe(0);
    }

    [Fact]
    public async Task MissingStampClaim_AbortsTheConnection()
    {
        var h = Build(StoredUser(), sessionStamp: null);

        await Should.ThrowAsync<HubException>(async () => await h.Filter.InvokeMethodAsync(h.Invocation, h.Next));

        h.Context.Received(1).Abort();
    }

    [Fact]
    public async Task DeletedUser_AbortsTheConnection_AndRefusesTheCall()
    {
        var h = Build(storedUser: null, CurrentStamp);

        await Should.ThrowAsync<HubException>(async () => await h.Filter.InvokeMethodAsync(h.Invocation, h.Next));

        h.Context.Received(1).Abort();
        h.NextCalls.ShouldBe(0);
    }
}
