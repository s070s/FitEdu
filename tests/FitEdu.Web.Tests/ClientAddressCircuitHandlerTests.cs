using System.Net;
using Microsoft.AspNetCore.Http;
using FitEdu.Infrastructure.Services;
using FitEdu.Web.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class ClientAddressCircuitHandlerTests
{
    private static async Task<string?> AddressKeyAfterOpeningAsync(HttpContext? httpContext)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var clientAddress = new CircuitClientAddress();

        await new ClientAddressCircuitHandler(accessor, clientAddress).OnCircuitOpenedAsync(null!, CancellationToken.None);

        return clientAddress.Key;
    }

    private static DefaultHttpContext RequestFrom(string address)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return httpContext;
    }

    [Fact]
    public async Task OnCircuitOpened_StoresTheAddressOfTheConnection()
    {
        (await AddressKeyAfterOpeningAsync(RequestFrom("203.0.113.7"))).ShouldBe("203.0.113.7");
    }

    [Fact]
    public async Task OnCircuitOpened_IPv4SeenThroughADualStackSocket_IsStoredAsIPv4()
    {
        (await AddressKeyAfterOpeningAsync(RequestFrom("::ffff:203.0.113.7"))).ShouldBe("203.0.113.7");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task OnCircuitOpened_LoopbackAddress_IsStoredAsUnknown(string address)
    {
        (await AddressKeyAfterOpeningAsync(RequestFrom(address))).ShouldBeNull();
    }

    [Fact]
    public async Task OnCircuitOpened_NoHttpContextOrNoAddress_IsStoredAsUnknown()
    {
        (await AddressKeyAfterOpeningAsync(null)).ShouldBeNull();
        (await AddressKeyAfterOpeningAsync(new DefaultHttpContext())).ShouldBeNull();
    }
}
