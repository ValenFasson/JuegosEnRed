using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhotonRoomBrowser : MonoBehaviourPunCallbacks
{
    private static readonly string[] FixedRoomNames =
    {
        "Sala 1",
        "Sala 2",
        "Sala 3",
        "Sala 4"
    };

    [SerializeField] private string gameVersion = "0.1";

    private readonly Dictionary<string, RoomInfo> roomsByName = new Dictionary<string, RoomInfo>(StringComparer.Ordinal);
    private static string sessionUserId;
    private string roomToRecover;
    private bool intentionalDisconnect;
    private bool returnToLobby;
    private bool recovering;
    private int reconnectAttempts;
    private float recoveryStartedAt;
    private float nextReconnectAttempt;
    private float attemptStartedAt;
    private bool roomRequestPending;
    private bool receivedRoomList;
    private int observedRound = -1;
    private GamePhase? observedPhase;
    private bool observedResult;
    private readonly PhotonFeedbackHistory feedback = new PhotonFeedbackHistory();
    private readonly PhotonPlayerFeedback playerFeedback = new PhotonPlayerFeedback();

    public static PhotonRoomBrowser Instance { get; private set; }

    public IReadOnlyList<string> RoomNames => FixedRoomNames;
    public int MaxPlayersPerRoom => PropHuntRoundRules.RequiredPlayers;
    public string StatusMessage { get; private set; } = "Iniciando Photon...";
    public PhotonProcessState ProcessState { get; private set; } = PhotonProcessState.Starting;
    public PhotonFeedbackSeverity StatusSeverity { get; private set; }
    public IReadOnlyList<PhotonFeedbackNotice> Notices => feedback.Notices;
    public bool IsRecovering => recovering;
    public int ReconnectAttempts => reconnectAttempts;
    public string RecoveryRoom => roomToRecover;
    public DisconnectCause? LastDisconnectCause { get; private set; }
    public bool CanUseLobby => PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InLobby && !PhotonNetwork.InRoom && !recovering && !roomRequestPending;

    public event Action StatusChanged;
    public event Action FeedbackChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        sessionUserId = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        Connect();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Connect()
    {
        if (recovering || roomRequestPending ||
            (ProcessState == PhotonProcessState.Connecting && !PhotonNetwork.IsConnectedAndReady))
            return;
        intentionalDisconnect = false;
        if (PhotonNetwork.InRoom)
        {
            SetStatus($"Estás en '{PhotonNetwork.CurrentRoom.Name}'.", PhotonProcessState.InRoom);
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            JoinLobbyIfNeeded();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            SetStatus("Conectando a Photon...", PhotonProcessState.Connecting);
            return;
        }

        SetStatus("Conectando a Photon...", PhotonProcessState.Connecting);

        if (!ConnectWithProtocol(gameVersion))
            ReportError("No se pudo iniciar la conexión. Revisá la configuración y volvé a intentarlo.", PhotonProcessState.Disconnected);
    }

    public bool JoinOrCreateRoom(string requestedName)
    {
        if (!TryGetFixedRoomName(requestedName, out string roomName))
        {
            ReportError("Esa sala no está entre las cuatro disponibles.");
            return false;
        }

        if (!CanUseLobby)
        {
            Notify("Esperá a que termine la operación actual para seleccionar una sala.", PhotonFeedbackSeverity.Warning);
            return false;
        }

        RoomOptions options = CreateRoomOptions();

        if (roomsByName.TryGetValue(roomName, out RoomInfo room) &&
            !PropHuntRoundRules.CanEnterRoom(room.IsOpen, room.PlayerCount, room.MaxPlayers))
        {
            ReportError(room.IsOpen ? "La sala está llena. Elegí otra sala." : "La sala está cerrada. Elegí otra sala.");
            return false;
        }

        roomRequestPending = true;
        SetStatus($"Entrando a '{roomName}'...", PhotonProcessState.JoiningRoom);
        roomToRecover = null;

        if (PhotonNetwork.JoinOrCreateRoom(
            roomName,
            options,
            TypedLobby.Default
        ))
        {
            return true;
        }

        roomRequestPending = false;
        ReportError("No se pudo enviar la solicitud para entrar a la sala. Intentá nuevamente.", PhotonProcessState.Lobby);
        return false;
    }

    public bool TryGetRoomInfo(string roomName, out RoomInfo roomInfo)
    {
        return roomsByName.TryGetValue(roomName, out roomInfo);
    }

    public void LeaveCurrentRoom()
    {
        if (!PhotonNetwork.InRoom || ProcessState == PhotonProcessState.LeavingRoom)
            return;

        SetStatus($"Saliendo de '{PhotonNetwork.CurrentRoom.Name}'...", PhotonProcessState.LeavingRoom);
        recovering = false;
        if (PhotonNetwork.LeaveRoom(false)) // Explicit abandonment, not a soft disconnect.
            roomToRecover = null;
        else
            ReportError("No se pudo enviar la solicitud para salir. Intentá nuevamente.", PhotonProcessState.InRoom);
    }

    public override void OnConnectedToMaster()
    {
        if (recovering)
        {
            if (!PhotonNetwork.RejoinRoom(roomToRecover))
                FailRecovery("No se pudo solicitar el reingreso.");
            return;
        }
        returnToLobby = false;
        JoinLobbyIfNeeded();
    }

    public override void OnJoinedLobby()
    {
        roomRequestPending = false;
        roomsByName.Clear();
        receivedRoomList = false;
        SetStatus("Buscando salas disponibles...", PhotonProcessState.SearchingRooms);
        PhotonLocalValidation.JoinOnce(this);
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        foreach (RoomInfo room in roomList)
        {
            if (room.RemovedFromList)
                roomsByName.Remove(room.Name);
            else if (TryGetFixedRoomName(room.Name, out _))
                roomsByName[room.Name] = room;
        }
        receivedRoomList = true;
        if (ProcessState == PhotonProcessState.SearchingRooms)
            SetStatus("Conectado. Elegí una de las cuatro salas.", PhotonProcessState.Lobby);
    }

    public override void OnCreatedRoom()
    {
        Notify($"Se creó '{PhotonNetwork.CurrentRoom.Name}'.");
    }

    public override void OnJoinedRoom()
    {
        bool recovered = recovering || PhotonNetwork.LocalPlayer.HasRejoined;
        recovering = false;
        returnToLobby = false;
        reconnectAttempts = 0;
        roomToRecover = PhotonNetwork.CurrentRoom.Name;
        roomsByName.Clear();
        roomRequestPending = false;
        ResetRoomFeedback(recovered);
        foreach (Player player in PhotonNetwork.PlayerList)
            playerFeedback.Remember(player.ActorNumber, player.NickName);
        ObserveRoomFeedback(false);
        SetStatus($"Estás en '{PhotonNetwork.CurrentRoom.Name}'.", PhotonProcessState.InRoom);
        if (recovered && TankGame.IsParticipant(PhotonNetwork.LocalPlayer.ActorNumber) &&
            TankGame.GetPlayerState(PhotonNetwork.LocalPlayer.ActorNumber) == PropHuntPlayerState.Alive &&
            TankGame.Phase != GamePhase.Finished)
            Notify("Recuperaste la conexión. Preparando tu regreso a la partida.");
        else if (TankGame.Phase != GamePhase.Waiting && !TankGame.IsParticipant(PhotonNetwork.LocalPlayer.ActorNumber))
            Notify($"Entraste a '{PhotonNetwork.CurrentRoom.Name}'. Esperá la próxima ronda para jugar.");
        else
            Notify($"Entraste a '{PhotonNetwork.CurrentRoom.Name}'.");
    }

    public override void OnLeftRoom()
    {
        // Realtime calls this BEFORE OnDisconnected on transport loss, too.
        // Only an intentional room leave transitions straight back to the Master server.
        if (PropHuntRoundRules.PreserveRecoveryRoom(roomToRecover != null,
                PhotonNetwork.NetworkClientState == ClientState.DisconnectingFromGameServer,
                intentionalDisconnect))
            return;
        roomToRecover = null;
        recovering = false;
        roomRequestPending = false;
        ResetRoomFeedback();
        SetStatus("Volviendo al lobby...", PhotonProcessState.JoiningLobby);
        JoinLobbyIfNeeded();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        roomRequestPending = false;
        Debug.LogWarning($"[Photon] CreateRoom failed ({returnCode}): {message}");
        ReportError(PhotonFeedbackText.RoomError(returnCode), PhotonProcessState.Lobby);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        roomRequestPending = false;
        Debug.LogWarning($"[Photon] JoinRoom failed ({returnCode}): {message}");
        if (recovering)
        {
            FailRecovery(PhotonFeedbackText.RoomError(returnCode));
            return;
        }
        ReportError(PhotonFeedbackText.RoomError(returnCode), PhotonProcessState.Lobby);
    }

    public override void OnCustomAuthenticationFailed(string debugMessage)
    {
        Debug.LogWarning($"[Photon] Custom authentication failed: {debugMessage}");
        ReportError("No se pudo validar tu sesión. Revisá tus datos de acceso.");
    }

    public override void OnErrorInfo(ErrorInfo errorInfo)
    {
        Debug.LogWarning($"[Photon] Server error: {errorInfo.Info}");
        ReportError("Photon informó un error del servidor. Si la conexión se interrumpe, intentaremos recuperarla.");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        LastDisconnectCause = cause;
        roomRequestPending = false;
        Debug.LogWarning($"[Photon] Disconnected: {cause}");
        roomsByName.Clear();
        if (intentionalDisconnect || cause == DisconnectCause.ApplicationQuit)
            return;
        if (returnToLobby)
        {
            returnToLobby = false;
            Connect();
            return;
        }
        if (recovering)
        {
            if (!CanRecover(cause) && cause != DisconnectCause.DisconnectByClientLogic)
                FailRecovery(PhotonFeedbackText.Disconnect(cause));
            else
            {
                nextReconnectAttempt = Time.realtimeSinceStartup + Mathf.Min(reconnectAttempts, 4);
                SetStatus($"Reconectando a '{roomToRecover}'...", PhotonProcessState.Recovering, PhotonFeedbackSeverity.Warning);
            }
            return;
        }
        if (roomToRecover != null && CanRecover(cause))
        {
            recovering = true;
            recoveryStartedAt = Time.realtimeSinceStartup;
            reconnectAttempts = 0;
            nextReconnectAttempt = recoveryStartedAt + 0.5f;
            SetStatus($"Conexión perdida. Intentando volver a '{roomToRecover}'...", PhotonProcessState.Recovering, PhotonFeedbackSeverity.Warning);
            Notify("Se perdió tu conexión. Intentando recuperar la partida.", PhotonFeedbackSeverity.Warning);
            return;
        }
        ResetRoomFeedback();
        string disconnectMessage = PhotonFeedbackText.Disconnect(cause);
        if (cause == DisconnectCause.DisconnectByClientLogic)
            SetStatus(disconnectMessage, PhotonProcessState.Disconnected);
        else
            ReportError(disconnectMessage, PhotonProcessState.Disconnected);
    }

    private void Update()
    {
        int noticeCount = feedback.Notices.Count;
        feedback.Expire(Time.realtimeSinceStartupAsDouble);
        if (noticeCount != feedback.Notices.Count)
            FeedbackChanged?.Invoke();
        if (!recovering)
            return;
        float now = Time.realtimeSinceStartup;
        PeerStateValue peerState = PhotonNetwork.NetworkingClient.LoadBalancingPeer.PeerState;
        bool fullyDisconnected = peerState == PeerStateValue.Disconnected &&
                                 PhotonNetwork.NetworkClientState == ClientState.Disconnected;
        // Retry for the whole grace window; fast failures must not exhaust it early.
        if (now - recoveryStartedAt >= PropHuntRoundRules.RecoveryWindowSeconds)
        {
            FailRecovery("Se agotó el tiempo de reconexión.");
            return;
        }
        if (fullyDisconnected && now >= nextReconnectAttempt)
        {
            reconnectAttempts++;
            attemptStartedAt = now;
            nextReconnectAttempt = now + Mathf.Min(reconnectAttempts, 4);
            SetStatus($"Reconectando... intento {reconnectAttempts}.", PhotonProcessState.Recovering, PhotonFeedbackSeverity.Warning);
            if (!PhotonNetwork.ReconnectAndRejoin() && !PhotonNetwork.Reconnect())
            {
                ConnectWithProtocol(gameVersion);
            }
        }
        else if (peerState != PeerStateValue.Disconnected && peerState != PeerStateValue.Disconnecting &&
                 now - attemptStartedAt > 6f)
        {
            PhotonNetwork.Disconnect();
        }
    }

    private void FailRecovery(string reason)
    {
        recovering = false;
        roomToRecover = null;
        roomRequestPending = false;
        ReportError(reason + " Volviendo al lobby.", PhotonProcessState.JoiningLobby);
        // A failed rejoin already returns to the Master server; no need to reconnect it.
        if (PhotonNetwork.IsConnectedAndReady &&
            PhotonNetwork.NetworkingClient.Server == ServerConnection.MasterServer)
            JoinLobbyIfNeeded();
        else
        {
            returnToLobby = true;
            if (PhotonNetwork.NetworkingClient.LoadBalancingPeer.PeerState == PeerStateValue.Disconnected)
            {
                returnToLobby = false;
                Connect();
            }
            else
                PhotonNetwork.Disconnect();
        }
    }

    private void OnApplicationQuit()
    {
        intentionalDisconnect = true;
        recovering = false;
    }

    private static bool CanRecover(DisconnectCause cause)
    {
        return cause == DisconnectCause.ClientTimeout || cause == DisconnectCause.ServerTimeout ||
               cause == DisconnectCause.Exception || cause == DisconnectCause.ExceptionOnConnect ||
               cause == DisconnectCause.DnsExceptionOnConnect || cause == DisconnectCause.SendException ||
               cause == DisconnectCause.ReceiveException || cause == DisconnectCause.DisconnectByServerReasonUnknown;
    }

    public static void ConfigureIdentity()
    {
        // Unique per running client, stable across network reconnects. Parallel clients must not share an ID.
        // Preserve a real authenticated account ID if one is already provided.
        if (sessionUserId == null)
            sessionUserId = Guid.NewGuid().ToString("N");
        if (PhotonNetwork.AuthValues == null || string.IsNullOrEmpty(PhotonNetwork.AuthValues.UserId))
            PhotonNetwork.AuthValues = new AuthenticationValues(sessionUserId);
        if (string.IsNullOrWhiteSpace(PhotonNetwork.NickName))
            PhotonNetwork.NickName = "Jugador" + sessionUserId.Substring(0, 6);
    }

    public static bool ConnectWithProtocol(string baseVersion)
    {
        ConfigureIdentity();
        if (PhotonNetwork.PhotonServerSettings == null)
            return false;
        // PUN 2.55's ConnectUsingSettings overwrites GameVersion/AppVersion from AppSettings.
        // Pass a runtime copy instead of editing the project settings asset or setting GameVersion too early.
        AppSettings settings = CreateProtocolSettings(PhotonNetwork.PhotonServerSettings.AppSettings, baseVersion);
        return PhotonNetwork.ConnectUsingSettings(settings, PhotonNetwork.PhotonServerSettings.StartInOfflineMode);
    }

    public static AppSettings CreateProtocolSettings(AppSettings source, string baseVersion)
    {
        AppSettings settings = source.CopyTo(new AppSettings());
        settings.AppVersion = baseVersion + ".sync" + PropHuntRoundRules.Protocol + "_" + PhotonNetwork.PunVersion;
        if (PhotonLocalValidation.Active)
            settings.FixedRegion = PhotonLocalValidation.FixedRegion;
        return settings;
    }

    public static RoomOptions CreateRoomOptions()
    {
        return new RoomOptions
        {
            MaxPlayers = (byte)PropHuntRoundRules.RequiredPlayers,
            IsOpen = true,
            IsVisible = true,
            PlayerTtl = PropHuntRoundRules.PlayerTtlMilliseconds,
            EmptyRoomTtl = PropHuntRoundRules.EmptyRoomTtlMilliseconds,
            CleanupCacheOnLeave = true,
            BroadcastPropsChangeToAll = true,
            DeleteNullProperties = true
        };
    }

    private void JoinLobbyIfNeeded()
    {
        if (PhotonNetwork.InRoom)
            return;

        if (PhotonNetwork.InLobby)
        {
            SetStatus(receivedRoomList ? "Conectado. Elegí una de las cuatro salas." : "Buscando salas disponibles...",
                receivedRoomList ? PhotonProcessState.Lobby : PhotonProcessState.SearchingRooms);
            PhotonLocalValidation.JoinOnce(this);
            return;
        }

        SetStatus("Entrando al lobby...", PhotonProcessState.JoiningLobby);

        if (!PhotonNetwork.JoinLobby(TypedLobby.Default))
            ReportError("No se pudo entrar al lobby. Intentá conectarte nuevamente.");
    }

    private static bool TryGetFixedRoomName(
        string requestedName,
        out string roomName)
    {
        foreach (string fixedRoomName in FixedRoomNames)
        {
            if (!string.Equals(
                requestedName,
                fixedRoomName,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                continue;
            }

            roomName = fixedRoomName;
            return true;
        }

        roomName = null;
        return false;
    }

    private void SetStatus(string message, PhotonProcessState? state = null,
        PhotonFeedbackSeverity severity = PhotonFeedbackSeverity.Info)
    {
        PhotonProcessState nextState = state ?? ProcessState;
        if (StatusMessage == message && ProcessState == nextState && StatusSeverity == severity)
            return;
        ProcessState = nextState;
        StatusSeverity = severity;
        StatusMessage = message;
        Debug.Log($"[Photon] {message}");
        StatusChanged?.Invoke();
    }

    private void ReportError(string message, PhotonProcessState? state = null)
    {
        SetStatus(message, state, PhotonFeedbackSeverity.Error);
        Notify(message, PhotonFeedbackSeverity.Error);
    }

    public void Notify(string message, PhotonFeedbackSeverity severity = PhotonFeedbackSeverity.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        feedback.Add(message, severity, Time.realtimeSinceStartupAsDouble);
        Debug.Log($"[Photon feedback] {message}");
        FeedbackChanged?.Invoke();
    }

    public string PlayerName(int actor) => playerFeedback.Name(actor);

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Notify(playerFeedback.Entered(newPlayer.ActorNumber, newPlayer.NickName, newPlayer.HasRejoined));
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Notify(playerFeedback.Left(otherPlayer.ActorNumber, otherPlayer.NickName, otherPlayer.IsInactive),
            PhotonFeedbackSeverity.Warning);
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        playerFeedback.Remember(targetPlayer.ActorNumber, targetPlayer.NickName);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        playerFeedback.Remember(newMasterClient.ActorNumber, newMasterClient.NickName);
        Notify($"{playerFeedback.Name(newMasterClient.ActorNumber)} pasó a coordinar la partida.");
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        ObserveRoomFeedback(true);
    }

    private void ResetRoomFeedback(bool preservePlayers = false)
    {
        if (preservePlayers)
            playerFeedback.ResetRound();
        else
            playerFeedback.Clear();
        observedRound = -1;
        observedPhase = null;
        observedResult = false;
    }

    private void ObserveRoomFeedback(bool announce)
    {
        if (!PhotonNetwork.InRoom || !(PhotonNetwork.CurrentRoom.CustomProperties["phase"] is int phaseValue))
            return;
        bool hadSnapshot = observedPhase.HasValue;
        int round = TankGame.CurrentRound;
        GamePhase phase = (GamePhase)phaseValue;
        if (observedRound != round)
        {
            playerFeedback.ResetRound();
            observedRound = round;
            observedResult = false;
        }
        if (PhotonNetwork.CurrentRoom.CustomProperties["participants"] is int[] actors)
            foreach (int actor in actors)
            {
                Player player = PhotonNetwork.CurrentRoom.GetPlayer(actor);
                if (player != null)
                    playerFeedback.Remember(actor, player.NickName);
                // A cancelled hunter round retains the old actor state in gameplay properties.
                // Interpret the confirmed cancellation consistently on every snapshot.
                PropHuntPlayerState state = phase == GamePhase.Finished &&
                    TankGame.EndReason == "HunterDisconnected" &&
                    TankGame.TryGetShooterActorNumber(out int hunterActor) && actor == hunterActor
                    ? PropHuntPlayerState.Abandoned : TankGame.GetPlayerState(actor);
                Notify(playerFeedback.ObserveState(actor, state, announce && hadSnapshot),
                    state == PropHuntPlayerState.Abandoned ? PhotonFeedbackSeverity.Warning : PhotonFeedbackSeverity.Info);
            }
        if (announce && hadSnapshot && observedPhase != phase)
            Notify(phase == GamePhase.Playing && observedPhase == GamePhase.Paused
                ? "Se reanudó la partida." : PhotonFeedbackText.Phase(phase) + ".");
        // Confirmed room properties also survive missed cosmetic events and Master changes.
        if (phase == GamePhase.Finished && !observedResult)
        {
            if (announce && hadSnapshot)
            {
                Notify(PhotonFeedbackText.Result(TankGame.WinnerTeam, TankGame.EndReason));
            }
            observedResult = true;
        }
        observedPhase = phase;
    }

}
