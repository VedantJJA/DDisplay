using DDisplay.Core.Protocol;
using DDisplay.Core.Transport;

namespace DDisplay.Tests.Transport;

/// <summary>
/// In-memory transport for unit testing the session-layer logic without network.
/// </summary>
public sealed class MockTransport : ITransport
{
    private readonly Queue<(string json, string type)> _receivedMessages = new();
    private readonly Queue<(byte[] nalUnits, bool isKeyframe, long ts)> _sentFrames = new();

    public bool IsConnected { get; private set; }
    public string DisplayName => "Mock";

    public event EventHandler<ControlMessageReceivedEventArgs>? ControlMessageReceived;
    public event EventHandler<TransportDisconnectedEventArgs>? Disconnected;
    public event EventHandler? Connected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        Connected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        Disconnected?.Invoke(this, new TransportDisconnectedEventArgs
        {
            Reason = "MockDisconnect",
        });
        return Task.CompletedTask;
    }

    public Task SendControlMessageAsync(ControlMessage message, CancellationToken cancellationToken = default)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(message, message.GetType());
        _receivedMessages.Enqueue((json, message.Type));
        return Task.CompletedTask;
    }

    public Task SendMediaFrameAsync(ReadOnlyMemory<byte> nalUnits, bool isKeyframe,
        long presentationTimestampMs, CancellationToken cancellationToken = default)
    {
        _sentFrames.Enqueue((nalUnits.ToArray(), isKeyframe, presentationTimestampMs));
        return Task.CompletedTask;
    }

    /// <summary>Simulates an incoming control message from the Android client.</summary>
    public void SimulateIncomingMessage(string json, string type)
    {
        ControlMessageReceived?.Invoke(this, new ControlMessageReceivedEventArgs
        {
            RawJson = json,
            MessageType = type,
        });
    }

    /// <summary>Returns true if a control message of the given type was sent.</summary>
    public bool WasSent(string type) =>
        _receivedMessages.Any(m => m.type == type);

    public int SentFrameCount => _sentFrames.Count;

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
