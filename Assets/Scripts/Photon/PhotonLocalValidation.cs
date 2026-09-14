using System;
using System.IO;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

// Opt-in launch configuration. Normal clients continue to use the existing lobby flow.
public sealed class PhotonLocalValidation : MonoBehaviour
{
    public const string FixedRoom = "Sala 1";
    public const string FixedRegion = "sa";
    public const string EditorLogEnvironment = "PropHuntGame_VALIDATION_EDITOR_LOG";
    public static bool Active { get; private set; }
    public static string ClientLabel { get; private set; }
    public static string LogPath { get; private set; }
    private static bool joinRequested;
    private StreamWriter editorLog;
    private readonly object logLock = new object();
    private float nextSnapshot;
    private string lastSnapshot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = false;
        ClientLabel = null;
        LogPath = null;
        joinRequested = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Application.isEditor)
        {
            LogPath = Environment.GetEnvironmentVariable(EditorLogEnvironment);
            Active = !string.IsNullOrEmpty(LogPath);
            ClientLabel = "Editor";
        }
        else
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-tankValidation" && i + 1 < args.Length)
                {
                    Active = true;
                    ClientLabel = args[++i];
                }
                else if (args[i] == "-logFile" && i + 1 < args.Length)
                    LogPath = args[++i];
            }
        }
        if (!Active)
            return;
        Application.runInBackground = true;
        PhotonNetwork.NickName = ClientLabel;
        var host = new GameObject("PropHuntGame Local Validation");
        DontDestroyOnLoad(host);
        host.AddComponent<PhotonLocalValidation>();
    }

    private void Awake()
    {
        if (Application.isEditor)
        {
            editorLog = new StreamWriter(LogPath, false, Encoding.UTF8) { AutoFlush = true };
            Application.logMessageReceivedThreaded += CaptureEditorLog;
        }
        Debug.Log($"[Validation] {ClientLabel}; room={FixedRoom}; region={FixedRegion}; log={LogPath}");
    }

    private void CaptureEditorLog(string message, string stackTrace, LogType type)
    {
        lock (logLock)
        {
            if (editorLog == null)
                return;
            editorLog.WriteLine($"{DateTime.UtcNow:O} [{type}] {message}");
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                editorLog.WriteLine(stackTrace);
        }
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= CaptureEditorLog;
        lock (logLock)
        {
            editorLog?.Dispose();
            editorLog = null;
        }
    }

    public static void JoinOnce(PhotonRoomBrowser browser)
    {
        // Never recreate/rejoin after voluntary leave or exhausted recovery.
        if (!Active || joinRequested)
            return;
        joinRequested = true;
        if (!browser.JoinOrCreateRoom(FixedRoom))
            Debug.LogError("[Validation] Automatic join failed. Check the room/lobby diagnostics; join manually to retry.");
    }

    public static string DescribeState(bool includeCountdown = true)
    {
        PhotonRoomBrowser browser = PhotonRoomBrowser.Instance;
        var text = new StringBuilder();
        text.AppendLine($"Connection: {PhotonNetwork.NetworkClientState} | Region: {PhotonNetwork.CloudRegion}");
        text.AppendLine($"Reconnect: {(browser != null && browser.IsRecovering ? "recovering" : "idle")} | Rejoined: {PhotonNetwork.LocalPlayer.HasRejoined}");
        if (browser != null)
        {
            text.AppendLine($"Reconnect attempts: {browser.ReconnectAttempts} | Window: {PropHuntRoundRules.RecoveryWindowSeconds:0}s | Target: {browser.RecoveryRoom ?? "none"} | Last disconnect: {browser.LastDisconnectCause?.ToString() ?? "none"}");
            text.AppendLine(browser.StatusMessage);
        }
        if (PropHuntGame.Instance != null && !string.IsNullOrEmpty(PropHuntGame.Instance.ConfigurationError))
            text.AppendLine("CONFIGURATION ERROR: " + PropHuntGame.Instance.ConfigurationError);
        if (!PhotonNetwork.InRoom)
        {
            text.Append("Round / Master / hunter / phase / player states: unavailable (outside room)");
            return text.ToString();
        }
        Room room = PhotonNetwork.CurrentRoom;
        PropHuntGame.TryGetShooterActorNumber(out int hunter);
        text.AppendLine($"Room: {room.Name} | Players: {room.PlayerCount}/{room.MaxPlayers} | Local: #{PhotonNetwork.LocalPlayer.ActorNumber}");
        text.AppendLine($"Round: {PropHuntGame.CurrentRound} | Revision: {PropHuntGame.ReadInt("revision")} | Phase: {PropHuntGame.Phase}");
        text.AppendLine($"Master: #{room.MasterClientId} | Hunter: {(hunter > 0 ? "#" + hunter : "pending")} | Alive props: {PropHuntGame.AlivePropCount}");
        // Include missing participants as well as retained inactive actors.
        var actors = new System.Collections.Generic.SortedSet<int>(room.Players.Keys);
        if (room.CustomProperties["participants"] is int[] participants)
            foreach (int actor in participants)
                actors.Add(actor);
        foreach (int actor in actors)
        {
            Player player = room.GetPlayer(actor);
            bool participant = PropHuntGame.IsParticipant(actor);
            string state = participant ? PropHuntGame.GetPlayerState(actor).ToString() : "waiting";
            string connection = player == null ? "missing" : player.IsInactive ? "inactive" : "active";
            int readyRound = player != null && player.CustomProperties[PropHuntGame.ReadyRoundKey] is int rr ? rr : 0;
            int readyToken = player != null && player.CustomProperties[PropHuntGame.ReadyTokenKey] is int rt ? rt : -1;
            text.AppendLine($"#{actor} {player?.NickName} {(actor == hunter ? "hunter" : participant ? "prop" : "unassigned")} | {state} | {connection}");
            text.AppendLine($"  Ready round/token: {readyRound}/{readyToken} | Recovery token: {PropHuntGame.RecoveryToken(actor)} | Reconnect deadline: {PropHuntGame.ReconnectDeadline(actor):F3}");
            double deadline = PropHuntGame.ReconnectDeadline(actor);
            if (includeCountdown && deadline > 0d)
                text.AppendLine($"  Reconnect grace remaining: {Math.Max(0d, deadline - PhotonNetwork.Time):F1}s");
        }
        text.Append($"Winner: {PropHuntGame.WinnerTeam} | End reason: {PropHuntGame.EndReason}");
        return text.ToString();
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < nextSnapshot)
            return;
        nextSnapshot = Time.realtimeSinceStartup + 0.25f;
        string snapshot = DescribeState(false);
        if (snapshot == lastSnapshot)
            return;
        lastSnapshot = snapshot;
        Debug.Log("[Validation state]\n" + snapshot);
    }
}
