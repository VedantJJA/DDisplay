using DDisplay.Core.Protocol;

namespace DDisplay.Core.Transport;

/// <summary>
/// Abstraction over all transport implementations (ADB-USB, USB-tether, Wi-Fi).
/// The capture, encode, and session-management layers use only this interface.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>True when the transport is connected and ready to send/receive.</summary>
    bool IsConnected { get; }

    /// <summary>Human-readable name for logging and UI (e.g., "USB-ADB", "Wi-Fi").</summary>
    string DisplayName { get; }

    /// <summary>
    /// Establishes the connection. Throws TransportException on failure.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects. Safe to call when already disconnected.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a control message to the remote end. Thread-safe.
    /// </summary>
    Task SendControlMessageAsync(ControlMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an encoded media frame. Thread-safe.
    /// Should be called from the encoding pipeline, not the control message path.
    /// </summary>
    Task SendMediaFrameAsync(
        ReadOnlyMemory<byte> nalUnits,
        bool isKeyframe,
        long presentationTimestampMs,
        CancellationToken cancellationToken = default);

    /// <summary>Raised when a control message is received from the Android client.</summary>
    event EventHandler<ControlMessageReceivedEventArgs>? ControlMessageReceived;

    /// <summary>Raised when the transport disconnects (cable unplugged, network error, etc.).</summary>
    event EventHandler<TransportDisconnectedEventArgs>? Disconnected;

    /// <summary>Raised when the transport successfully connects.</summary>
    event EventHandler? Connected;
}

public sealed class ControlMessageReceivedEventArgs : EventArgs
{
    public required string RawJson { get; init; }
    public required string MessageType { get; init; }
}

public sealed class TransportDisconnectedEventArgs : EventArgs
{
    public required string Reason { get; init; }
    public Exception? Exception { get; init; }
}

public sealed class TransportException : Exception
{
    public TransportException(string message) : base(message) { }
    public TransportException(string message, Exception inner) : base(message, inner) { }
}
