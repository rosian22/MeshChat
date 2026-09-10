namespace MeshChat.Network;

using Zeroconf;

/// <summary>
/// Handles mDNS/Bonjour peer discovery on the local network.
/// Registers the MeshChat service and discovers other MeshChat instances.
/// </summary>
public class MdnsDiscovery
{
    private readonly int _port;
    private readonly string _peerId;
    private CancellationTokenSource? _browserCts;
    private bool _isRunning;

    private const string ServiceType = "_meshchat._tcp.local.";

    /// <summary>
    /// Raised when a peer is discovered on the network.
    /// Parameters: host, port, peerId.
    /// </summary>
    public event Action<string, int, string>? PeerDiscovered;

    /// <summary>
    /// Raised when a previously discovered peer is no longer available.
    /// </summary>
    public event Action<string>? PeerLost;

    /// <summary>
    /// Initializes a new instance of the <see cref="MdnsDiscovery"/> class.
    /// </summary>
    /// <param name="port">The TCP port to register the service on.</param>
    /// <param name="peerId">The unique identifier for this peer (typically a public key hash).</param>
    public MdnsDiscovery(int port, string peerId)
    {
        _port = port;
        _peerId = peerId;
        _isRunning = false;
    }

    /// <summary>
    /// Starts mDNS peer discovery (browsing for other MeshChat services).
    /// Note: Service registration requires platform-specific APIs (NSNetService on iOS,
    /// NsdManager on Android). This class handles the discovery/browsing side using Zeroconf.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _browserCts = new CancellationTokenSource();

        // Start continuous browsing loop
        _ = BrowsePeersAsync(_browserCts.Token);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops mDNS discovery.
    /// </summary>
    public Task StopAsync()
    {
        if (!_isRunning)
            return Task.CompletedTask;

        _isRunning = false;
        _browserCts?.Cancel();
        _browserCts?.Dispose();
        _browserCts = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Continuously browses for MeshChat peers on the local network.
    /// </summary>
    private async Task BrowsePeersAsync(CancellationToken cancellationToken)
    {
        var discoveredPeers = new Dictionary<string, (string host, int port)>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Resolve all hosts advertising the MeshChat service
                    var results = await ZeroconfResolver.ResolveAsync(
                        ServiceType,
                        scanTime: TimeSpan.FromSeconds(3),
                        cancellationToken: cancellationToken);

                    var currentPeers = new HashSet<string>();

                    foreach (var host in results)
                    {
                        // Each host may have multiple services
                        foreach (var service in host.Services)
                        {
                            if (!service.Key.Contains("_meshchat"))
                                continue;

                            var svc = service.Value;
                            var peerId = host.DisplayName;

                            // Try to get peerId from TXT record properties
                            if (svc.Properties != null)
                            {
                                foreach (var propSet in svc.Properties)
                                {
                                    if (propSet.TryGetValue("peerId", out var id))
                                    {
                                        peerId = id;
                                        break;
                                    }
                                }
                            }

                            // Skip ourselves
                            if (peerId == _peerId)
                                continue;

                            currentPeers.Add(peerId);

                            if (!discoveredPeers.ContainsKey(peerId))
                            {
                                var ipAddress = host.IPAddresses?.FirstOrDefault() ?? host.DisplayName;
                                var port = svc.Port;
                                discoveredPeers[peerId] = (ipAddress, port);
                                PeerDiscovered?.Invoke(ipAddress, port, peerId);
                            }
                        }
                    }

                    // Detect lost peers
                    var lostPeers = discoveredPeers.Keys.Except(currentPeers).ToList();
                    foreach (var lostPeerId in lostPeers)
                    {
                        discoveredPeers.Remove(lostPeerId);
                        PeerLost?.Invoke(lostPeerId);
                    }

                    // Wait before next scan
                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS browse error: {ex.Message}");
                    await Task.Delay(5000, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }
}
