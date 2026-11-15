using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Plugin.Gear.Hid.Windows;

/// <summary>
/// An overlapped HID handle: output reports go out through <c>HidD_SetOutputReport</c>, input
/// reports are read through an async <see cref="FileStream"/> with a cancellable timeout.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsHidConnection : IHidConnection
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private readonly int _inputLength;

    public WindowsHidConnection(SafeFileHandle handle, int inputLength)
    {
        _handle = handle;
        _inputLength = inputLength;
        // bufferSize 0: no FileStream buffering, every read is one input report.
        _stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
    }

    public unsafe void SendOutputReport(ReadOnlySpan<byte> report)
    {
        fixed (byte* p = report)
        {
            if (!HidNative.HidD_SetOutputReport(_handle, p, (uint)report.Length))
            {
                throw new IOException($"HidD_SetOutputReport failed (error {Marshal.GetLastWin32Error()}).");
            }
        }
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        byte[] report = new byte[Math.Max(_inputLength, buffer.Length)];
        using CancellationTokenSource cts = new(timeout);
        try
        {
            // Cancellation issues CancelIoEx on the pending read, so nothing stays queued.
            int count = _stream.ReadAsync(report, 0, report.Length, cts.Token).GetAwaiter().GetResult();
            int copied = Math.Min(count, buffer.Length);
            report.AsSpan(0, copied).CopyTo(buffer);
            return copied;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _handle.Dispose();
    }
}
