using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhotonSessionManager : MonoBehaviourPunCallbacks
{
    public static PhotonSessionManager Instance { get; private set; }

    [Header("Connection")]
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private string gameVersion = "0.1";

    [Header("Development Room")]
    [SerializeField] private string roomName = "DevRoom";
    [SerializeField, Range(1, 16)] private int maxPlayers = 4;

    private bool joinRoomAfterConnect;

    public bool IsInRoom => PhotonNetwork.InRoom;
    public bool IsMasterClient => PhotonNetwork.IsMasterClient;

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
        if (connectOnStart)
        {
            ConnectAndJoin();
        }
    }

    public void ConnectAndJoin()
    {
        joinRoomAfterConnect = true;

        if (PhotonNetwork.InRoom)
        {
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            JoinLobbyOrRoom();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            return;
        }

        PhotonNetwork.GameVersion = gameVersion;
        PhotonNetwork.ConnectUsingSettings();
    }

    public void LeaveRoom()
    {
        joinRoomAfterConnect = false;

        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
        }
    }

    public void Disconnect()
    {
        joinRoomAfterConnect = false;

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Photon] Connected to Master Server.");

        if (joinRoomAfterConnect)
        {
            JoinLobbyOrRoom();
        }
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("[Photon] Joined lobby.");

        if (joinRoomAfterConnect)
        {
            JoinConfiguredRoom();
        }
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[Photon] Joined room '{PhotonNetwork.CurrentRoom.Name}'. Players: {PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}");
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Photon] Left room.");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Photon] Could not join room. Code: {returnCode}. {message}");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Photon] Could not create room. Code: {returnCode}. {message}");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Photon] Disconnected. Cause: {cause}");
    }

    private void JoinLobbyOrRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            return;
        }

        if (PhotonNetwork.InLobby)
        {
            JoinConfiguredRoom();
            return;
        }

        PhotonNetwork.JoinLobby();
    }

    private void JoinConfiguredRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            return;
        }

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = (byte)Mathf.Clamp(maxPlayers, 1, 16),
            IsOpen = true,
            IsVisible = true
        };

        PhotonNetwork.JoinOrCreateRoom(roomName, roomOptions, TypedLobby.Default);
    }
}
