using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PropHuntRoomManager :MonoBehaviourPunCallbacks
{
    public static PropHuntRoomManager Instance
    {
        get;
        private set;
    }

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

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private string lobbySceneName = "Lobby";

    public bool IsReturningToLobby { get; private set; }

    private readonly Dictionary<string, RoomInfo> rooms =
        new Dictionary<string, RoomInfo>(
            StringComparer.Ordinal
        );

    private bool roomRequestPending;

    public IReadOnlyList<string> RoomNames =>
        FixedRoomNames;

    public int MaxPlayersPerRoom =>
        PropHuntRoundRules.MaxPlayers;

    public string StatusMessage
    {
        get;
        private set;
    } = "Iniciando Photon...";

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

    public bool CanStartGame =>
        PhotonNetwork.InRoom &&
        PhotonNetwork.IsMasterClient &&
        PropHuntRoundRules.CanStartGame(
            PhotonNetwork.CurrentRoom.PlayerCount
        );

    private void Awake()
    {
        if (Instance != null &&
            Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(
            gameObject
        );

        PhotonNetwork.AutomaticallySyncScene =
            true;
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
                    (byte)PropHuntRoundRules.MaxPlayers,

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

    public bool ReturnToLobby()
    {
        if (IsReturningToLobby)
            return true;

        if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
        {
            SetStatus(
                "La escena de lobby no está habilitada en Build Settings.",
                PhotonFeedbackSeverity.Error
            );
            return false;
        }

        IsReturningToLobby = true;
        SetStatus("Saliendo de la room para volver al lobby...");

        if (!PhotonNetwork.InRoom)
        {
            LoadLobbyScene();
            Connect();
            return true;
        }

        // Abandonar la room, incluso si permite jugadores inactivos/reconexión.
        if (PhotonNetwork.LeaveRoom(false))
            return true;

        IsReturningToLobby = false;
        SetStatus("No se pudo salir de la room. Intentá nuevamente.",
            PhotonFeedbackSeverity.Error);
        return false;
    }

    private void LoadLobbyScene()
    {
        IsReturningToLobby = false;
        roomRequestPending = false;
        rooms.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Carga local después de salir: no cambia la escena de la room anterior.
        SceneManager.LoadScene(lobbySceneName);
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

    // --------------------------------------------------
    // START GAME - NUEVO
    // --------------------------------------------------

    public void StartGame()
    {
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning(
                "[Photon] No estás dentro de una Room."
            );

            return;
        }

        // Solamente el Master puede iniciar.
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning(
                "[Photon] Solo el Master puede iniciar la partida."
            );

            return;
        }

        int playerCount =
            PhotonNetwork.CurrentRoom.PlayerCount;

        if (!PropHuntRoundRules.CanStartGame(
            playerCount))
        {
            Debug.LogWarning(
                "[Photon] No se puede iniciar la partida."
            );

            return;
        }

        Debug.Log(
            $"[Photon] El Master inició la partida " +
            $"con {playerCount} jugador(es)."
        );

        // Nadie más puede entrar una vez
        // comenzada la partida.
        PhotonNetwork.CurrentRoom.IsOpen =
            false;

        PhotonNetwork.CurrentRoom.IsVisible =
            false;

        SetStatus(
            $"Iniciando partida con {playerCount} jugador(es)..."
        );

        PhotonNetwork.LoadLevel(
            gameSceneName
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
            if (!IsFixedRoomName(
                room.Name))
            {
                continue;
            }

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
            $"{PropHuntRoundRules.MaxPlayers}"
        );

        UpdateRoomStatus();

        // IMPORTANTE:
        // YA NO SE INICIA AUTOMÁTICAMENTE.
    }

    public override void OnPlayerEnteredRoom(
        Player newPlayer)
    {
        Debug.Log(
            $"[Photon] Entró {newPlayer.NickName}. " +
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PropHuntRoundRules.MaxPlayers}"
        );

        UpdateRoomStatus();

        // IMPORTANTE:
        // NO llamar StartGame() acá.
    }

    public override void OnPlayerLeftRoom(
        Player otherPlayer)
    {
        Debug.Log(
            $"[Photon] Salió {otherPlayer.NickName}."
        );

        if (!PhotonNetwork.InRoom)
            return;

        if (SceneManager
            .GetActiveScene()
            .name == gameSceneName)
        {
            return;
        }

        UpdateRoomStatus();
    }

    public override void OnLeftRoom()
    {
        roomRequestPending = false;

        SetStatus(
            "Volviendo al Lobby..."
        );

        if (IsReturningToLobby)
            LoadLobbyScene();

        // Photon regresa al Master Server y OnConnectedToMaster entra al lobby.
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

        if (IsReturningToLobby)
            LoadLobbyScene();
    }

    // --------------------------------------------------
    // INFO
    // --------------------------------------------------

    public bool TryGetRoomInfo(
        string roomName,
        out RoomInfo roomInfo)
    {
        return rooms.TryGetValue(
            roomName,
            out roomInfo
        );
    }

    private void UpdateRoomStatus()
    {
        if (!PhotonNetwork.InRoom)
            return;

        int currentPlayers =
            PhotonNetwork.CurrentRoom.PlayerCount;

        SetStatus(
            $"{PhotonNetwork.CurrentRoom.Name} - " +
            $"{currentPlayers}/" +
            $"{PropHuntRoundRules.MaxPlayers} jugadores"
        );
    }

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