using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Networking;

// UDP solo para encontrar el host; la conexion y todas las acciones usan ENet.
public sealed class LanRoomDiscovery : IDisposable
{
    private readonly PacketPeerUdp _udp = new();
    private string _code = "", _nonce = "";
    private int _gamePort;
    private bool _hosting;
    private ulong _nextQuery, _deadline, _replySecond;
    private int _replyCount;
    private string[] _targets = Array.Empty<string>();
    public readonly record struct SearchResult(string? Address, int Port, string? Error);

    public Error Advertise(string code, int gamePort)
    {
        Error error = _udp.Bind(RoomCodeProtocol.DiscoveryPort, "0.0.0.0", 65536);
        if (error != Error.Ok) return error;
        _code = code; _gamePort = gamePort; _hosting = true;
        return Error.Ok;
    }

    public Error Search(string code, ulong now)
    {
        Error error = _udp.Bind(0, "0.0.0.0", 65536);
        if (error != Error.Ok) return error;
        _udp.SetBroadcastEnabled(true);
        _code = code; _nonce = Guid.NewGuid().ToString("N");
        _deadline = now + 5000; _nextQuery = now;
        _targets = BroadcastTargets();
        return Error.Ok;
    }

    public SearchResult? Poll(ulong now, bool acceptingPlayers = true)
    {
        if (!_hosting && now >= _deadline)
            return new SearchResult(null, 0, "No se encontró la sala. Revisa el código, que esté abierta y que ambos estén en la misma red.");
        if (!_hosting && now >= _nextQuery)
        {
            byte[] query = Encoding.ASCII.GetBytes(RoomCodeProtocol.Query(_code, _nonce));
            foreach (string target in _targets)
                if (_udp.SetDestAddress(target, RoomCodeProtocol.DiscoveryPort) == Error.Ok) _udp.PutPacket(query);
            _nextQuery = now + 500;
        }
        for (int i = 0; i < 32 && _udp.GetAvailablePacketCount() > 0; i++)
        {
            byte[] packet = _udp.GetPacket();
            string source = _udp.GetPacketIP();
            int sourcePort = _udp.GetPacketPort();
            if (packet.Length > RoomCodeProtocol.MaxPacketBytes ||
                !RoomCodeProtocol.TryRead(Encoding.ASCII.GetString(packet), out bool query,
                    out string code, out string nonce, out int gamePort) || code != _code) continue;
            if (_hosting && query && acceptingPlayers)
            {
                if (_replySecond != now / 1000) { _replySecond = now / 1000; _replyCount = 0; }
                if (++_replyCount > 24) continue;
                if (_udp.SetDestAddress(source, sourcePort) == Error.Ok)
                    _udp.PutPacket(Encoding.ASCII.GetBytes(RoomCodeProtocol.Answer(_code, nonce, _gamePort)));
            }
            else if (!_hosting && !query && nonce == _nonce && IsHostAddress(source))
                return new SearchResult(source, gamePort, null);
        }
        return null;
    }

    private static bool IsHostAddress(string text) => IPAddress.TryParse(text, out IPAddress? address) &&
        address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] is > 0 and < 224 &&
        !address.Equals(IPAddress.Broadcast);

    private static string[] BroadcastTargets()
    {
        var targets = new HashSet<string>(StringComparer.Ordinal) { "255.255.255.255", "127.0.0.1" };
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                if (adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                        if (info.Address.AddressFamily == AddressFamily.InterNetwork && info.IPv4Mask is { } mask)
                        {
                            byte[] address = info.Address.GetAddressBytes(), netmask = mask.GetAddressBytes();
                            targets.Add(new IPAddress(address.Select((b, i) => (byte)(b | ~netmask[i])).ToArray()).ToString());
                        }
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
        return targets.Take(16).ToArray();
    }

    public void Dispose() { _udp.Close(); _udp.Dispose(); }
}
