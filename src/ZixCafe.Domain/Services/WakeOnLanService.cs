using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ZixCafe.Domain.Services;

public static partial class WakeOnLanService
{
    [GeneratedRegex(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^([0-9A-Fa-f]{12})$")]
    private static partial Regex MacRegex();

    public static byte[] BuildMagicPacket(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            throw new ArgumentException("MAC address cannot be empty.", nameof(macAddress));
        }

        var cleaned = macAddress.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
        if (cleaned.Length != 12)
        {
            throw new FormatException($"Invalid MAC address format: '{macAddress}'. Expected 12 hexadecimal characters.");
        }

        var macBytes = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            if (!byte.TryParse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out macBytes[i]))
            {
                throw new FormatException($"Invalid hex in MAC address: '{macAddress}'");
            }
        }

        // Magic Packet is 6 bytes of 0xFF followed by 16 repetitions of the 6-byte target MAC address (102 bytes total).
        var packet = new byte[102];
        Array.Fill(packet, (byte)0xFF, 0, 6);

        for (var i = 0; i < 16; i++)
        {
            Buffer.BlockCopy(macBytes, 0, packet, 6 + (i * 6), 6);
        }

        return packet;
    }

    public static async Task<bool> SendMagicPacketAsync(string macAddress, string broadcastSubnet = "255.255.255.255", int port = 9)
    {
        try
        {
            var packet = BuildMagicPacket(macAddress);
            using var client = new UdpClient();
            client.EnableBroadcast = true;

            var targetIp = IPAddress.TryParse(broadcastSubnet, out var ip) ? ip : IPAddress.Broadcast;
            var endPoint = new IPEndPoint(targetIp, port);

            await client.SendAsync(packet, packet.Length, endPoint);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidMacAddress(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return false;
        return MacRegex().IsMatch(macAddress.Trim());
    }
}
