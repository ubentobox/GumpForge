using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace GumpForge.App.Services;

public class ServerLinkService
{
    private static ServerLinkService? _instance;
    public static ServerLinkService Instance => _instance ??= new ServerLinkService();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private bool _isConnected;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStateChanged?.Invoke(value);
            }
        }
    }

    public string ConnectedGMName { get; private set; } = string.Empty;

    public event Action<bool>? ConnectionStateChanged;
    public event Action<bool, string>? AuthCompleted;
    public event Action<string, int, List<KeyValuePair<string, string>>>? TargetAcquired;
    public event Action<string>? GumpReceived;
    public event Action<string>? NotificationReceived;
    public event Action<string>? LogMessage;

    private ServerLinkService() { }

    public async Task ConnectAsync(string host, int port, string username, string password)
    {
        Disconnect();

        _client = new TcpClient();
        _cts = new CancellationTokenSource();

        Log(string.Format("Connecting to {0}:{1}...", host, port));
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();
        IsConnected = true;

        // Start read loop
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));

        // Send auth packet
        Log(string.Format("Authenticating as '{0}'...", username));
        
        // Escape variables safely for basic JSON
        string escapedUser = username.Replace("\"", "\\\"");
        string escapedPass = password.Replace("\"", "\\\"");
        var authJson = string.Format("{{\"username\":\"{0}\",\"password\":\"{1}\"}}", escapedUser, escapedPass);
        SendPacket(0x01, authJson);
    }

    public void Disconnect()
    {
        if (!IsConnected) return;

        Log("Disconnecting...");
        _cts?.Cancel();
        _stream?.Close();
        _client?.Close();
        IsConnected = false;
        ConnectedGMName = string.Empty;
        Log("Disconnected.");
    }

    public void RequestTarget()
    {
        if (!IsConnected) return;
        Log("Requesting target cursor in-game...");
        SendPacket(0x02, "{}");
    }

    public void TriggerDoubleClick(int playerSerial, int itemSerial)
    {
        if (!IsConnected) return;
        Log($"Requesting double-click of item {itemSerial} as player {playerSerial}...");
        var json = $"{{\"playerSerial\":{playerSerial},\"itemSerial\":{itemSerial}}}";
        SendPacket(0x03, json);
    }

    private void SendPacket(byte packetId, string json)
    {
        if (_stream == null || !IsConnected) return;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length + 1));

            _stream.Write(lengthPrefix, 0, 4);
            _stream.WriteByte(packetId);
            _stream.Write(payload, 0, payload.Length);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            Log($"Send error: {ex.Message}");
            Disconnect();
        }
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        byte[] lengthBuffer = new byte[4];
        while (IsConnected && !token.IsCancellationRequested)
        {
            try
            {
                if (_stream == null) break;

                // Read length prefix
                if (!await ReadExactlyAsync(_stream, lengthBuffer, 4, token))
                    break;

                int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBuffer, 0));
                if (length <= 0 || length > 5 * 1024 * 1024) // 5MB cap
                {
                    Log("Received invalid packet length.");
                    break;
                }

                byte[] payload = new byte[length];
                if (!await ReadExactlyAsync(_stream, payload, length, token))
                    break;

                byte packetId = payload[0];
                string json = Encoding.UTF8.GetString(payload, 1, length - 1);

                HandlePacket(packetId, json);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log($"Read error: {ex.Message}");
                }
                break;
            }
        }

        Disconnect();
    }

    private async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int size, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < size)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, size - totalRead), token);
            if (read <= 0) return false;
            totalRead += read;
        }
        return true;
    }

    private void HandlePacket(byte packetId, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            switch (packetId)
            {
                case 0x81: // AuthResponse
                    {
                        bool success = root.GetProperty("success").GetBoolean();
                        if (success)
                        {
                            ConnectedGMName = root.GetProperty("gmName").GetString() ?? "GM";
                            Log($"Successfully authenticated as {ConnectedGMName}.");
                            AuthCompleted?.Invoke(true, string.Empty);
                        }
                        else
                        {
                            string error = root.GetProperty("errorMessage").GetString() ?? "Unknown authentication error";
                            Log($"Authentication failed: {error}");
                            AuthCompleted?.Invoke(false, error);
                            Disconnect();
                        }
                    }
                    break;

                case 0x82: // TargetAcquired
                    {
                        string name = root.GetProperty("name").GetString() ?? "Unknown";
                        int serial = root.GetProperty("serial").GetInt32();
                        var propList = new List<KeyValuePair<string, string>>();

                        if (root.TryGetProperty("properties", out var propsVal) && propsVal.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var propEl in propsVal.EnumerateArray())
                            {
                                string propName = propEl.GetProperty("name").GetString() ?? "";
                                string propVal = propEl.GetProperty("value").GetString() ?? "";
                                if (!string.IsNullOrEmpty(propName))
                                {
                                    propList.Add(new KeyValuePair<string, string>(propName, propVal));
                                }
                            }
                        }

                        Log($"Target acquired: {name} (Serial: {serial})");
                        TargetAcquired?.Invoke(name, serial, propList);
                    }
                    break;

                case 0x83: // GumpReceived
                    {
                        Log("Gump layout received from server.");
                        GumpReceived?.Invoke(json);
                    }
                    break;

                case 0x84: // Notification
                    {
                        string msg = root.GetProperty("message").GetString() ?? "";
                        Log($"Server message: {msg}");
                        NotificationReceived?.Invoke(msg);
                    }
                    break;

                default:
                    Log($"Unknown packet ID: {packetId:X2}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to parse packet payload: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
