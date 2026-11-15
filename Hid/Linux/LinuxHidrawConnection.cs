using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Plugin.Gear.Hid.Linux;

/// <summary>An open /dev/hidrawN node: <c>write()</c> for output reports, <c>poll()</c> + <c>read()</c> for input.</summary>
internal sealed partial class LinuxHidrawConnection : IHidConnection
{
    private const int ReadWrite = 2;
    private const int CloseOnExec = 0x80000;
    private const int Eperm = 1;
    private const int Eintr = 4;
    private const int Eagain = 11;
    private const int Eacces = 13;
    private const short PollIn = 0x1;
    private const short PollError = 0x8 | 0x10 | 0x20; // POLLERR | POLLHUP | POLLNVAL

    private readonly SafeFileHandle _handle;

    private LinuxHidrawConnection(SafeFileHandle handle)
    {
        _handle = handle;
    }

    /// <exception cref="HidAccessException">The node exists but may not be opened.</exception>
    public static IHidConnection Open(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("hidraw is only available on Linux.");
        }

        int fd = NativeOpen(path, ReadWrite | CloseOnExec);
        if (fd < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno is Eacces or Eperm)
            {
                throw new HidAccessException($"Access to HID device '{path}' was denied.");
            }

            throw new IOException($"Could not open HID device '{path}' (errno {errno}).");
        }

        return new LinuxHidrawConnection(new SafeFileHandle(fd, ownsHandle: true));
    }

    public unsafe void SendOutputReport(ReadOnlySpan<byte> report)
    {
        // If write() gets no answer on some kernels/devices, ioctl HIDIOCSOUTPUT (kernel 5.11+)
        // would be the fallback. Deliberately not implemented until a real device shows it is needed.
        fixed (byte* p = report)
        {
            nint written = NativeWrite(_handle, p, (nuint)report.Length);
            if (written != report.Length)
            {
                throw new IOException($"write() to hidraw failed (errno {Marshal.GetLastPInvokeError()}).");
            }
        }
    }

    public unsafe int Read(Span<byte> buffer, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            int remaining = (int)Math.Clamp(deadline - Environment.TickCount64, 0, int.MaxValue);
            PollFd fd = new() { Fd = (int)_handle.DangerousGetHandle(), Events = PollIn };
            int ready = NativePoll(ref fd, 1, remaining);
            if (ready < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                if (errno != Eintr)
                {
                    throw new IOException($"poll() on hidraw failed (errno {errno}).");
                }

                if (Environment.TickCount64 >= deadline)
                {
                    return 0;
                }

                continue;
            }

            if (ready == 0)
            {
                return 0;
            }

            if ((fd.Revents & PollError) != 0 && (fd.Revents & PollIn) == 0)
            {
                throw new IOException("The HID device was disconnected.");
            }

            nint count;
            fixed (byte* p = buffer)
            {
                count = NativeRead(_handle, p, (nuint)buffer.Length);
            }

            if (count >= 0)
            {
                return (int)count;
            }

            int readErrno = Marshal.GetLastPInvokeError();
            if (readErrno is not (Eintr or Eagain))
            {
                throw new IOException($"read() on hidraw failed (errno {readErrno}).");
            }
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int NativeOpen(string path, int flags);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int NativePoll(ref PollFd fds, nuint count, int timeoutMs);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint NativeRead(SafeFileHandle fd, byte* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static unsafe partial nint NativeWrite(SafeFileHandle fd, byte* buffer, nuint count);
}
