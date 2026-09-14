using ExitGames.Client.Photon;
using System;

public class PhotonConnectionTestSimulation : IDisposable
{
    private readonly PhotonPeer peer;
    private readonly bool originalEnabled;
    private readonly int originalIncomingLag, originalOutgoingLag;
    private readonly int originalIncomingJitter, originalOutgoingJitter;
    private readonly int originalIncomingLoss, originalOutgoingLoss;
    private bool configured;
    private int lag, jitter, loss;
    private bool disposed;

    public PhotonConnectionTestSimulation(PhotonPeer peer)
    {
        this.peer = peer ?? throw new ArgumentNullException(nameof(peer));
        NetworkSimulationSet settings = peer.NetworkSimulationSettings;
        originalEnabled = peer.IsSimulationEnabled;
        originalIncomingLag = settings.IncomingLag;
        originalOutgoingLag = settings.OutgoingLag;
        originalIncomingJitter = settings.IncomingJitter;
        originalOutgoingJitter = settings.OutgoingJitter;
        originalIncomingLoss = settings.IncomingLossPercentage;
        originalOutgoingLoss = settings.OutgoingLossPercentage;
    }

    public void Apply(int lagMilliseconds, int jitterMilliseconds, int lossPercentage)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PhotonConnectionTestSimulation));
        lag = Math.Max(0, Math.Min(500, lagMilliseconds));
        jitter = Math.Max(0, Math.Min(lag, jitterMilliseconds));
        loss = Math.Max(0, Math.Min(100, lossPercentage));
        configured = true;
        Maintain();
    }

    public void Maintain()
    {
        if (!configured || disposed)
            return;
        NetworkSimulationSet settings = peer.NetworkSimulationSettings;
        settings.IncomingLag = settings.OutgoingLag = lag;
        settings.IncomingJitter = settings.OutgoingJitter = jitter;
        settings.IncomingLossPercentage = settings.OutgoingLossPercentage = loss;
        bool enabled = lag > 0 || jitter > 0 || loss > 0;
        // Keep a cut active through reconnect attempts, without restarting simulation each frame.
        if (peer.IsSimulationEnabled != enabled)
            peer.IsSimulationEnabled = enabled;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!configured)
            return;
        peer.IsSimulationEnabled = false;
        NetworkSimulationSet settings = peer.NetworkSimulationSettings;
        settings.IncomingLag = originalIncomingLag;
        settings.OutgoingLag = originalOutgoingLag;
        settings.IncomingJitter = originalIncomingJitter;
        settings.OutgoingJitter = originalOutgoingJitter;
        settings.IncomingLossPercentage = originalIncomingLoss;
        settings.OutgoingLossPercentage = originalOutgoingLoss;
        peer.IsSimulationEnabled = originalEnabled;
    }
}
