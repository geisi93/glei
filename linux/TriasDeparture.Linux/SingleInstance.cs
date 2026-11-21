using System.Net.Sockets;

namespace TriasDeparture.Linux;

/// <summary>
/// One instance per user: an exclusive lock file decides who is first, a Unix socket carries the
/// "show the popup" signal from a second start. (Named EventWaitHandles don't exist on Linux.)
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream? _lock;
    private readonly string _socketPath;
    private Socket? _listener;

    /// <summary>EWOULDBLOCK from flock(): the lock is held by another process.</summary>
    private const int LockHeld = 11;

    /// <summary>
    /// True when this process runs the app: it holds the lock, or the lock could not be used and no
    /// running glei answers on the socket (then it runs without single instance rather than not at all).
    /// </summary>
    public bool IsFirst { get; }

    /// <summary>Why single instance is off, for the log (Acquire runs before logging is set up).</summary>
    public string? Problem { get; }

    private SingleInstance(bool isFirst, FileStream? lockFile, string socketPath, string? problem = null)
    {
        IsFirst     = isFirst;
        _lock       = lockFile;
        _socketPath = socketPath;
        Problem     = problem;
    }

    public static SingleInstance Acquire(string name, string? directory = null)
    {
        directory ??= LinuxPaths.RuntimeDirectory;
        var lockPath   = Path.Combine(directory, $"{name}.lock");
        var socketPath = Path.Combine(directory, $"{name}.sock");
        try
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // FileShare.None is an flock() on Unix: released by the kernel when the process dies, never stale
            var file = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(true, file, socketPath);
        }
        catch (IOException ex) when (ex.GetType() == typeof(IOException) && ex.HResult == LockHeld)
        {
            // Locked: another glei of this user is running
            return new SingleInstance(false, null, socketPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only, full, someone else's file … – no proof of a running instance. If one answers on
            // the socket anyway, let it show the popup instead of starting a second tray icon.
            return IsAnswering(socketPath)
                ? new SingleInstance(false, null, socketPath)
                : new SingleInstance(true, null, socketPath, $"lock {lockPath} unusable: {ex.Message}");
        }
    }

    private static bool IsAnswering(string socketPath)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return false;
        }
    }

    public void SignalFirst()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
            socket.Send("show"u8);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // The first instance is still starting up (or runs without socket) – nothing to show
        }
    }

    /// <summary>Calls <paramref name="onSignal"/> (on a pool thread) whenever another instance starts.</summary>
    /// <remarks>Never throws: without the socket, the app runs on and a second start just does nothing.</remarks>
    /// <returns>Why listening failed, or null.</returns>
    public string? Listen(Action onSignal)
    {
        // Also without the lock: a later start then finds this instance on the socket (see Acquire)
        if (!IsFirst) return null;

        Socket? listener = null;
        try
        {
            File.Delete(_socketPath); // left over from a crashed run (a live instance would have answered in Acquire)
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(_socketPath)); // ArgumentException when the path is too long
            listener.Listen(4);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            listener?.Dispose();
            return string.Join("; ", new[] { Problem, $"socket {_socketPath} unusable: {ex.Message}" }.OfType<string>());
        }

        _listener = listener;
        _ = AcceptLoopAsync(listener, onSignal);
        return Problem;
    }

    private static async Task AcceptLoopAsync(Socket listener, Action onSignal)
    {
        while (true)
        {
            try
            {
                using var client = await listener.AcceptAsync();
                onSignal();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // a client that hung up early – keep listening
            }
        }
    }

    public void Dispose()
    {
        if (_listener is not null)
        {
            _listener.Dispose();
            try { File.Delete(_socketPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        _lock?.Dispose();
    }
}
