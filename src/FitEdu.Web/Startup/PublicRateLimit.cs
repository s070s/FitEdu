using System.Net;
using System.Threading.RateLimiting;

namespace FitEdu.Web.Startup;

/// <summary>
/// The "public" rate limiter for the anonymous read endpoints (catalogue, exercises, blog, search…):
/// a fixed window per caller IP address. The app's own loopback calls are never limited: every
/// guest's Blazor page reaches the API through them, so one shared loopback bucket would throttle
/// every visitor at once. The test server reports no address at all and is not limited either.
/// </summary>
public static class PublicRateLimit
{
    public const string PolicyName = "public";

    public const int PermitsPerWindow = 120;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Every loopback and address-less request shares this key; its limiter never rejects.
    private const string UnlimitedPartitionKey = "unlimited";

    public static RateLimitPartition<string> PartitionFor(HttpContext httpContext)
    {
        var partitionKey = PartitionKeyFor(httpContext.Connection.RemoteIpAddress);
        if (partitionKey is null)
            return RateLimitPartition.GetNoLimiter(UnlimitedPartitionKey);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitsPerWindow,
            Window = Window,
            QueueLimit = 0
        });
    }

    // A provider hands one connection a whole /64, so a caller could otherwise pick a fresh address
    // per request and never reach the limit.
    private const int Ipv6PrefixBytes = 8;

    /// <summary>
    /// The caller's bucket: its address as text (an IPv6 address as its /64 network, "2001:db8::/64"),
    /// or null (not limited) for loopback or no address.
    /// An IPv4 address seen through a dual-stack socket (::ffff:1.2.3.4) counts as the IPv4 address.
    /// </summary>
    public static string? PartitionKeyFor(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
            return null;

        var address = remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress;
        if (IPAddress.IsLoopback(address))
            return null;

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.ToString();

        var networkBytes = new byte[16];
        address.GetAddressBytes().AsSpan(0, Ipv6PrefixBytes).CopyTo(networkBytes);
        return $"{new IPAddress(networkBytes)}/64";
    }
}
