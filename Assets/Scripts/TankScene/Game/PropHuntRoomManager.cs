using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PropHuntRoomManager : MonoBehaviourPunCallbacks
{
    public static PropHuntRoomManager Instance { get; private set; }

    [Header("Photon")]
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private string gameVersion = "0.1";

    [Header("Game")]
    [SerializeField] private string gameSceneName = "Game";

    private readonly Dictionary<string, RoomInfo> rooms =
        new Dictionary<string, RoomInfo>();

    public IEnumerable<RoomInfo> Rooms => rooms.Values;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        PhotonNetwork.AutomaticallySyncScene = true;
    }

    private void Start()
    {
        if (connectOnStart)
        {
            Connect();
        }
    }

    public void Connect()
    {
        if (PhotonNetwork.IsConnected)
            return;

        PhotonNetwork.GameVersion = gameVersion;

        PhotonNetwork.ConnectUsingSettings();
    }

    public void CreateRoom(string roomName)
    {
        if (!PhotonNetwork.InLobby)
        {
            Debug.LogWarning(
                "Todavía no estamos dentro del Lobby."
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(roomName))
            return;

        RoomOptions options =
            new RoomOptions
            {
                MaxPlayers =
                    PropHuntRoundRules.RequiredPlayers,

                IsOpen = true,
                IsVisible = true
            };

        PhotonNetwork.CreateRoom(
            roomName,
            options,
            TypedLobby.Default
        );
    }

    public void JoinRoom(string roomName)
    {
        if (!PhotonNetwork.InLobby)
            return;

        if (string.IsNullOrWhiteSpace(roomName))
            return;

        PhotonNetwork.JoinRoom(roomName);
    }

    public void LeaveRoom()
    {
        if (!PhotonNetwork.InRoom)
            return;

        PhotonNetwork.LeaveRoom();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log(
            "Conectado a Photon."
        );

        if (!PhotonNetwork.InLobby &&
            !PhotonNetwork.InRoom)
        {
            PhotonNetwork.JoinLobby();
        }
    }

    public override void OnJoinedLobby()
    {
        Debug.Log(
            "Entramos al Lobby."
        );

        rooms.Clear();
    }

    public override void OnRoomListUpdate(
        List<RoomInfo> roomList)
    {
        foreach (RoomInfo room in roomList)
        {
            if (room.RemovedFromList)
            {
                rooms.Remove(
                    room.Name
                );

                continue;
            }

            rooms[room.Name] = room;
        }
    }

    public override void OnJoinedRoom()
    {
        Debug.Log(
            $"Room: {PhotonNetwork.CurrentRoom.Name} - " +
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PropHuntRoundRules.RequiredPlayers}"
        );

        TryStartGame();
    }

    public override void OnLeftRoom()
    {
        Debug.Log(
            "Salimos de la Room."
        );

        if (PhotonNetwork.IsConnectedAndReady &&
            !PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }
    }

    public override void OnPlayerEnteredRoom(
        Player newPlayer)
    {
        TryStartGame();
    }

    public override void OnPlayerLeftRoom(
        Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (SceneManager.GetActiveScene().name ==
            gameSceneName)
        {
            return;
        }

        PhotonNetwork.CurrentRoom.IsOpen = true;
        PhotonNetwork.CurrentRoom.IsVisible = true;
    }

    private void TryStartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (!PropHuntRoundRules.CanStartGame(
            PhotonNetwork.CurrentRoom.PlayerCount))
        {
            return;
        }

        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        PhotonNetwork.LoadLevel(
            gameSceneName
        );
    }

    public override void OnJoinRoomFailed(
        short returnCode,
        string message)
    {
        Debug.LogError(
            PhotonFeedbackText.RoomError(
                returnCode
            )
        );
    }

    public override void OnCreateRoomFailed(
        short returnCode,
        string message)
    {
        Debug.LogError(
            PhotonFeedbackText.RoomError(
                returnCode
            )
        );
    }

    public override void OnDisconnected(
        DisconnectCause cause)
    {
        rooms.Clear();

        Debug.LogWarning(
            PhotonFeedbackText.Disconnect(
                cause
            )
        );
    }
}