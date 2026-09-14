using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PropHuntRoomManager : MonoBehaviourPunCallbacks
{
    public static PropHuntRoomManager Instance { get; private set; }

    private static readonly string[] FixedRoomNames =
    {
        "Sala 1",
        "Sala 2",
        "Sala 3",
        "Sala 4"
    };

    [Header("Photon")]
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private string gameVersion = "0.1";

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "Game";

    private readonly Dictionary<string, RoomInfo> rooms =
        new Dictionary<string, RoomInfo>(
            StringComparer.Ordinal
        );

    private bool roomRequestPending;

    public IReadOnlyList<string> RoomNames =>
        FixedRoomNames;

    public int MaxPlayersPerRoom =>
        PropHuntRoundRules.RequiredPlayers;

    public string StatusMessage { get; private set; } =
        "Iniciando Photon...";

    public PhotonFeedbackSeverity StatusSeverity
    {
        get;
        private set;
    } = PhotonFeedbackSeverity.Info;

    public bool CanUseLobby =>
        PhotonNetwork.IsConnectedAndReady &&
        PhotonNetwork.InLobby &&
        !PhotonNetwork.InRoom &&
        !roomRequestPending;

    private void Awake()
    {
        if (Instance != null &&
            Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        // Fundamental para que todos sigan al Master
        // cuando se carga Game.
        PhotonNetwork.AutomaticallySyncScene = true;
    }

    private void Start()
    {
        if (connectOnStart)
        {
            Connect();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // --------------------------------------------------
    // CONNECTION
    // --------------------------------------------------

    public void Connect()
    {
        if (PhotonNetwork.InRoom)
        {
            SetStatus(
                $"Ya estás en '{PhotonNetwork.CurrentRoom.Name}'."
            );

            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            JoinLobbyIfNeeded();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            SetStatus(
                "Conectando a Photon..."
            );

            return;
        }

        SetStatus(
            "Conectando a Photon..."
        );

        PhotonNetwork.GameVersion =
            gameVersion;

        bool started =
            PhotonNetwork.ConnectUsingSettings();

        if (!started)
        {
            SetStatus(
                "No se pudo iniciar la conexión con Photon.",
                PhotonFeedbackSeverity.Error
            );
        }
    }

    private void JoinLobbyIfNeeded()
    {
        if (PhotonNetwork.InRoom)
            return;

        if (PhotonNetwork.InLobby)
        {
            SetStatus(
                "Conectado. Elegí una sala."
            );

            return;
        }

        SetStatus(
            "Entrando al Lobby..."
        );

        PhotonNetwork.JoinLobby(
            TypedLobby.Default
        );
    }

    // --------------------------------------------------
    // ROOMS
    // --------------------------------------------------

    public void JoinOrCreateRoom(
        string requestedRoomName)
    {
        if (!TryGetFixedRoomName(
            requestedRoomName,
            out string roomName))
        {
            SetStatus(
                "La sala seleccionada no es válida.",
                PhotonFeedbackSeverity.Error
            );

            return;
        }

        if (!CanUseLobby)
        {
            SetStatus(
                "Esperá a estar conectado al Lobby.",
                PhotonFeedbackSeverity.Warning
            );

            return;
        }

        if (rooms.TryGetValue(
            roomName,
            out RoomInfo room))
        {
            if (!PropHuntRoundRules.CanEnterRoom(
                room.IsOpen,
                room.PlayerCount,
                room.MaxPlayers))
            {
                SetStatus(
                    room.IsOpen
                        ? "La sala está llena."
                        : "La sala está cerrada.",
                    PhotonFeedbackSeverity.Warning
                );

                return;
            }
        }

        RoomOptions options =
            new RoomOptions
            {
                MaxPlayers =
                    (byte)PropHuntRoundRules
                        .RequiredPlayers,

                IsOpen = true,
                IsVisible = true,

                CleanupCacheOnLeave = true
            };

        roomRequestPending = true;

        SetStatus(
            $"Entrando a '{roomName}'..."
        );

        bool requestSent =
            PhotonNetwork.JoinOrCreateRoom(
                roomName,
                options,
                TypedLobby.Default
            );

        if (!requestSent)
        {
            roomRequestPending = false;

            SetStatus(
                "No se pudo enviar la solicitud de sala.",
                PhotonFeedbackSeverity.Error
            );
        }
    }

    public void LeaveRoom()
    {
        if (!PhotonNetwork.InRoom)
            return;

        SetStatus(
            "Saliendo de la sala..."
        );

        PhotonNetwork.LeaveRoom();
    }

    public bool TryGetRoomInfo(
        string roomName,
        out RoomInfo roomInfo)
    {
        return rooms.TryGetValue(
            roomName,
            out roomInfo
        );
    }

    // --------------------------------------------------
    // PHOTON CALLBACKS
    // --------------------------------------------------

    public override void OnConnectedToMaster()
    {
        Debug.Log(
            "[Photon] OnConnectedToMaster"
        );

        SetStatus(
            "Conectado a Photon."
        );

        JoinLobbyIfNeeded();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log(
            "[Photon] OnJoinedLobby"
        );

        roomRequestPending = false;

        rooms.Clear();

        SetStatus(
            "Conectado. Elegí una sala."
        );
    }

    public override void OnRoomListUpdate(
        List<RoomInfo> roomList)
    {
        foreach (RoomInfo room in roomList)
        {
            if (!IsFixedRoomName(room.Name))
                continue;

            if (room.RemovedFromList)
            {
                rooms.Remove(
                    room.Name
                );
            }
            else
            {
                rooms[room.Name] =
                    room;
            }
        }

        Debug.Log(
            $"[Photon] Room list actualizada. " +
            $"Salas conocidas: {rooms.Count}"
        );
    }

    public override void OnCreatedRoom()
    {
        Debug.Log(
            $"[Photon] Se creó " +
            $"'{PhotonNetwork.CurrentRoom.Name}'."
        );
    }

    public override void OnJoinedRoom()
    {
        roomRequestPending = false;

        Debug.Log(
            $"[Photon] OnJoinedRoom: " +
            $"{PhotonNetwork.CurrentRoom.Name} - " +
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PropHuntRoundRules.RequiredPlayers}"
        );

        UpdateRoomStatus();

        TryStartGame();
    }

    public override void OnPlayerEnteredRoom(
        Player newPlayer)
    {
        Debug.Log(
            $"[Photon] Entró {newPlayer.NickName}. " +
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PropHuntRoundRules.RequiredPlayers}"
        );

        UpdateRoomStatus();

        TryStartGame();
    }

    public override void OnPlayerLeftRoom(
        Player otherPlayer)
    {
        Debug.Log(
            $"[Photon] Salió {otherPlayer.NickName}."
        );

        if (!PhotonNetwork.InRoom)
            return;

        // En Game, las desconexiones las maneja
        // PropHuntGameManager.
        if (SceneManager
            .GetActiveScene()
            .name == gameSceneName)
        {
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.CurrentRoom.IsOpen =
                true;

            PhotonNetwork.CurrentRoom.IsVisible =
                true;
        }

        UpdateRoomStatus();
    }

    public override void OnLeftRoom()
    {
        roomRequestPending = false;

        Debug.Log(
            "[Photon] OnLeftRoom"
        );

        SetStatus(
            "Volviendo al Lobby..."
        );

        // Al volver al Master Server,
        // OnConnectedToMaster volverá a ejecutar
        // JoinLobbyIfNeeded().
    }

    public override void OnJoinRoomFailed(
        short returnCode,
        string message)
    {
        roomRequestPending = false;

        Debug.LogWarning(
            $"[Photon] JoinRoom failed " +
            $"({returnCode}): {message}"
        );

        SetStatus(
            PhotonFeedbackText.RoomError(
                returnCode
            ),
            PhotonFeedbackSeverity.Error
        );
    }

    public override void OnCreateRoomFailed(
        short returnCode,
        string message)
    {
        roomRequestPending = false;

        Debug.LogWarning(
            $"[Photon] CreateRoom failed " +
            $"({returnCode}): {message}"
        );

        SetStatus(
            PhotonFeedbackText.RoomError(
                returnCode
            ),
            PhotonFeedbackSeverity.Error
        );
    }

    public override void OnDisconnected(
        DisconnectCause cause)
    {
        roomRequestPending = false;

        rooms.Clear();

        Debug.LogWarning(
            $"[Photon] Desconectado: {cause}"
        );

        SetStatus(
            PhotonFeedbackText.Disconnect(
                cause
            ),
            PhotonFeedbackSeverity.Error
        );
    }

    // --------------------------------------------------
    // START GAME
    // --------------------------------------------------

    private void TryStartGame()
    {
        if (!PhotonNetwork.InRoom)
            return;

        if (!PhotonNetwork.IsMasterClient)
            return;

        if (!PropHuntRoundRules.CanStartGame(
            PhotonNetwork.CurrentRoom.PlayerCount))
        {
            return;
        }

        Debug.Log(
            "[Photon] 4/4 jugadores. " +
            "Cargando Game..."
        );

        PhotonNetwork.CurrentRoom.IsOpen =
            false;

        PhotonNetwork.CurrentRoom.IsVisible =
            false;

        SetStatus(
            "4/4 jugadores. Iniciando partida..."
        );

        PhotonNetwork.LoadLevel(
            gameSceneName
        );
    }

    private void UpdateRoomStatus()
    {
        if (!PhotonNetwork.InRoom)
            return;

        SetStatus(
            $"{PhotonNetwork.CurrentRoom.Name} - " +
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PropHuntRoundRules.RequiredPlayers} jugadores"
        );
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------

    private static bool TryGetFixedRoomName(
        string requestedName,
        out string roomName)
    {
        foreach (string fixedName in
                 FixedRoomNames)
        {
            if (string.Equals(
                requestedName,
                fixedName,
                StringComparison.OrdinalIgnoreCase))
            {
                roomName = fixedName;
                return true;
            }
        }

        roomName = null;
        return false;
    }

    private static bool IsFixedRoomName(
        string roomName)
    {
        return TryGetFixedRoomName(
            roomName,
            out _
        );
    }

    private void SetStatus(
        string message,
        PhotonFeedbackSeverity severity =
            PhotonFeedbackSeverity.Info)
    {
        StatusMessage = message;
        StatusSeverity = severity;

        Debug.Log(
            $"[Photon] {message}"
        );
    }
}