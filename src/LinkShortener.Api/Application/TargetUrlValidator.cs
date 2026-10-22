using System.Net;
using System.Net.Sockets;

namespace LinkShortener.Api.Application;

public interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class TargetUrlValidator(IHostAddressResolver resolver)
{
    public async Task<bool> IsValidAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IPAddress[] addresses;
        try
        {
            var host = uri.Host.Trim('[', ']');
            addresses = IPAddress.TryParse(host, out var literalAddress)
                ? [literalAddress]
                : await resolver.ResolveAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return false;
        }

        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xE0) == 0x20 &&
                !(bytes[0] == 0x20 && bytes[1] == 0x01 &&
                    bytes[2] == 0x0D && bytes[3] == 0xB8);
        }

        return address.AddressFamily == AddressFamily.InterNetwork &&
            !IsInRange(bytes, [0, 0, 0, 0], 8) &&
            !IsInRange(bytes, [10, 0, 0, 0], 8) &&
            !IsInRange(bytes, [100, 64, 0, 0], 10) &&
            !IsInRange(bytes, [127, 0, 0, 0], 8) &&
            !IsInRange(bytes, [169, 254, 0, 0], 16) &&
            !IsInRange(bytes, [172, 16, 0, 0], 12) &&
            !IsInRange(bytes, [192, 0, 0, 0], 24) &&
            !IsInRange(bytes, [192, 0, 2, 0], 24) &&
            !IsInRange(bytes, [192, 168, 0, 0], 16) &&
            !IsInRange(bytes, [198, 18, 0, 0], 15) &&
            !IsInRange(bytes, [198, 51, 100, 0], 24) &&
            !IsInRange(bytes, [203, 0, 113, 0], 24) &&
            bytes[0] < 224;
    }

    private static bool IsInRange(byte[] address, byte[] network, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        if (!address.AsSpan(0, fullBytes).SequenceEqual(network.AsSpan(0, fullBytes)))
        {
            return false;
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (address[fullBytes] & mask) == (network[fullBytes] & mask);
    }
}
