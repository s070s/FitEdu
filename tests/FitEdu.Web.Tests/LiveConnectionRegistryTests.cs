using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class LiveConnectionRegistryTests
{
    private static HubCallerContext Connection(string? userId, string connectionId)
    {
        var context = Substitute.For<HubCallerContext>();
        context.UserIdentifier.Returns(userId);
        context.ConnectionId.Returns(connectionId);
        return context;
    }

    [Fact]
    public void CloseAll_AbortsOnlyTheConnectionsOfThatUser()
    {
        var registry = new LiveConnectionRegistry();
        var firstOfMember = Connection("member", "conn-1");
        var secondOfMember = Connection("member", "conn-2");
        var ofOther = Connection("other", "conn-3");
        registry.Register(firstOfMember);
        registry.Register(secondOfMember);
        registry.Register(ofOther);

        registry.CloseAll("member");

        firstOfMember.Received(1).Abort();
        secondOfMember.Received(1).Abort();
        ofOther.DidNotReceive().Abort();
    }

    [Fact]
    public void CloseAll_SkipsAConnectionThatWasUnregistered()
    {
        var registry = new LiveConnectionRegistry();
        var closed = Connection("member", "conn-1");
        var open = Connection("member", "conn-2");
        registry.Register(closed);
        registry.Register(open);

        registry.Unregister(closed);
        registry.CloseAll("member");

        closed.DidNotReceive().Abort();
        open.Received(1).Abort();
    }

    [Fact]
    public void Unregister_OfTheLastConnection_RemovesTheUser_SoPastUsersAreNotKept()
    {
        var registry = new LiveConnectionRegistry();
        var first = Connection("member", "conn-1");
        var second = Connection("member", "conn-2");
        registry.Register(first);
        registry.Register(second);

        registry.Unregister(first);
        registry.UserCount.ShouldBe(1);
        registry.Unregister(second);

        registry.UserCount.ShouldBe(0);
    }

    [Fact]
    public void CloseAll_ForAUserWithoutConnections_DoesNothing()
    {
        var registry = new LiveConnectionRegistry();

        Should.NotThrow(() => registry.CloseAll("nobody"));
    }

    [Fact]
    public void RegisterAndUnregister_SkipAConnectionWithoutAUser()
    {
        var registry = new LiveConnectionRegistry();
        var anonymous = Connection(null, "conn-1");

        registry.Register(anonymous);
        registry.Unregister(anonymous);
        registry.CloseAll("member");

        anonymous.DidNotReceive().Abort();
    }
}
