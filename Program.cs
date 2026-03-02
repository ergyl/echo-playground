using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

const int tcpPort = 7;

var localAddresses = GetLocalIpAddresses();
if (localAddresses.Count == 0)
{
    return;
}

var listeners = new List<TcpListener>();
var acceptTasks = new List<Task>();
var clientTasks = new List<Task>();
var clientTasksLock = new object();
using var shutdownTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += HandleCancelKeyPress;

try
{
    Console.WriteLine("Starting TCP server...");

    foreach (var ip in localAddresses)
    {
        try
        {
            var listener = new TcpListener(ip, tcpPort);
            listener.Start();

            listeners.Add(listener);
            acceptTasks.Add(AcceptConnectionsAsync(listener, clientTasks, clientTasksLock, shutdownTokenSource.Token));

            Console.WriteLine($"Listening on {ip}:{tcpPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not bind {ip}:{tcpPort} - {ex.Message}");
        }
    }

    if (acceptTasks.Count == 0)
    {
        Console.WriteLine("No listeners were started. Server shutting down.");
        return;
    }

    Console.WriteLine("Server running. Press Ctrl+C to stop.");

    try
    {
        await Task.Delay(Timeout.Infinite, shutdownTokenSource.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Server shutdown signal received.");
    }
}
finally
{
    await shutdownTokenSource.CancelAsync();

    Console.CancelKeyPress -= HandleCancelKeyPress;

    foreach (var listener in listeners)
    {
        try
        {
            listener.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping listener: {ex.Message}");
        }
    }

    try
    {
        await Task.WhenAll(acceptTasks);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error while stopping accept loops: {ex.Message}");
    }

    Task[] pendingClientTasks;
    lock (clientTasksLock)
    {
        pendingClientTasks = clientTasks.ToArray();
    }

    try
    {
        await Task.WhenAll(pendingClientTasks);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error while stopping client handlers: {ex.Message}");
    }
}

void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    shutdownTokenSource.Cancel();
    Console.WriteLine("Shutdown requested. Stopping server...");
}

static async Task AcceptConnectionsAsync(
    TcpListener listener,
    List<Task> clientTasks,
    object clientTasksLock,
    CancellationToken cancellationToken)
{
    while (true)
    {
        var isCancellationRequested = cancellationToken.IsCancellationRequested;
        if (isCancellationRequested)
        {
            break;
        }

        TcpClient acceptedClient;
        try
        {
            acceptedClient = await listener.AcceptTcpClientAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Socket error while accepting a connection: {ex.Message}");
            continue;
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"Listener was disposed before accept completed: {ex.Message}");
            break;
        }

        var remoteEndpoint = acceptedClient.Client.RemoteEndPoint?.ToString() ?? "unknown remote endpoint";
        Console.WriteLine($"Accepted connection from: {remoteEndpoint}");

        var clientTask = HandleClientAsync(acceptedClient, cancellationToken);

        lock (clientTasksLock)
        {
            clientTasks.Add(clientTask);
        }

        _ = clientTask.ContinueWith(
            completedClientTask =>
            {
                lock (clientTasksLock)
                {
                    clientTasks.Remove(completedClientTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }
}

static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
{
    using (client)
    {
        try
        {
            var stream = client.GetStream();

            /* Memory block-based buffer to avoid unnecessary array allocations and copying on each read/write operation. 
             The buffer is reused for the entire client session (e.g. contiguous buffer) with buffer size of 4096 bytes (4 KB) */
            var buffer = new byte[4096];

            while (true)
            {
                var isCancellationRequested = cancellationToken.IsCancellationRequested;
                if (isCancellationRequested)
                {
                    break;
                }

                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Client handler canceled.");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Socket error while handling a client: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"Client connection was disposed: {ex.Message}");
        }
    }
}

static List<IPAddress> GetLocalIpAddresses()
{
    var addresses = NetworkInterface.GetAllNetworkInterfaces()
        .Where(i => i.OperationalStatus == OperationalStatus.Up)
        .SelectMany(i => i.GetIPProperties().UnicastAddresses)
        .Select(u => u.Address)
        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
        .Distinct()
        .ToList();

    var isIpv4LoopbackMissing = !addresses.Contains(IPAddress.Loopback);
    var isIpv6LoopbackMissing = !addresses.Contains(IPAddress.IPv6Loopback);

    if (isIpv4LoopbackMissing)
    {
        addresses.Add(IPAddress.Loopback);
    }

    if (isIpv6LoopbackMissing)
    {
        addresses.Add(IPAddress.IPv6Loopback);
    }

    return addresses;
}
