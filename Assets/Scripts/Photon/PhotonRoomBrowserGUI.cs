using System;
using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;

public sealed class PhotonRoomBrowserGUI :
    MonoBehaviourPunCallbacks
{
    [Serializable]
    private sealed class RoomSlotUI
    {
        public Text label;
        public Button joinButton;

        [NonSerialized]
        public string roomName;
    }

    [Header("Services")]
    [SerializeField] private PropHuntRoomManager roomManager;

    [Header("Status")]
    [SerializeField] private Text titleText;
    [SerializeField] private Text statusText;
    [SerializeField] private Button connectButton;

    private float statusHoldUntil;
    [Header("Lobby")]
    [SerializeField] private GameObject lobbyPanel;

    [SerializeField] private Text nicknameText;
    [SerializeField] private InputField nicknameInput;
    [SerializeField] private Button renameButton;

    [SerializeField] private InputField roomNameInput;
    [SerializeField] private Button createRoomButton;

    [SerializeField]
    private RoomSlotUI[] roomSlots =
        new RoomSlotUI[0];

    [Header("Current Room")]
    [SerializeField] private GameObject currentRoomPanel;

    [SerializeField] private Text roomText;
    [SerializeField] private Button leaveButton;

    private float nextRefreshTime;

    private void Start()
    {
        if (roomManager == null)
        {
            roomManager =
                PropHuntRoomManager.Instance;
        }

        if (roomManager == null)
        {
            Debug.LogError(
                "No se encontró PropHuntRoomManager.",
                this
            );

            enabled = false;
            return;
        }

        EnsureNickname();

        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime <
            nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.unscaledTime + 0.25f;

        Refresh();
    }

    // --------------------------------------------------
    // BOTONES UI
    // --------------------------------------------------

    public void Connect()
    {
        if (roomManager == null)
            return;

        SetStatus(
            "Conectando con Photon..."
        );

        roomManager.Connect();
    }

    public void CreateRoom()
    {
        if (roomManager == null ||
            roomNameInput == null)
        {
            return;
        }

        string roomName =
            roomNameInput.text.Trim();

        if (string.IsNullOrEmpty(roomName))
        {
            SetStatus(
                "Escribí un nombre para la sala."
            );

            return;
        }

        SetStatus(
            $"Creando sala '{roomName}'..."
        );

        roomManager.CreateRoom(
            roomName
        );
    }

    public void JoinRoom(string roomName)
    {
        if (roomManager == null)
            return;

        if (string.IsNullOrEmpty(roomName))
            return;

        SetStatus(
            $"Entrando a '{roomName}'..."
        );

        roomManager.JoinRoom(
            roomName
        );
    }

    public void LeaveRoom()
    {
        if (roomManager == null)
            return;

        roomManager.LeaveRoom();
    }

    public void ChangeNickname()
    {
        if (nicknameInput == null)
            return;

        string newName =
            nicknameInput.text
                .Trim()
                .Replace("\n", "")
                .Replace("\r", "");

        if (string.IsNullOrEmpty(newName))
        {
            SetStatus(
                "Escribí un nombre para tu jugador."
            );

            return;
        }

        if (newName.Length > 32)
        {
            newName =
                newName.Substring(
                    0,
                    32
                );
        }

        PhotonNetwork.NickName =
            newName;

        nicknameInput.SetTextWithoutNotify(
            newName
        );

        Refresh();
    }

    // --------------------------------------------------
    // PHOTON CALLBACKS
    // --------------------------------------------------

    public override void OnConnectedToMaster()
    {
        SetStatus(
            "Conectado a Photon."
        );

        Refresh();
    }

    public override void OnJoinedLobby()
    {
        SetStatus(
            "Lobby conectado. Elegí o creá una sala."
        );

        Refresh();
    }

    public override void OnJoinedRoom()
    {
        SetStatus(
            "Entraste a la sala."
        );

        Refresh();
    }

    public override void OnLeftRoom()
    {
        SetStatus(
            "Saliste de la sala."
        );

        Refresh();
    }

    public override void OnPlayerEnteredRoom(
        Player newPlayer)
    {
        Refresh();
    }

    public override void OnPlayerLeftRoom(
        Player otherPlayer)
    {
        Refresh();
    }

    public override void OnRoomListUpdate(
        List<RoomInfo> roomList)
    {
        Refresh();
    }

    public override void OnJoinRoomFailed(
        short returnCode,
        string message)
    {
        SetTemporaryStatus(
            PhotonFeedbackText.RoomError(
                returnCode
            )
        );

        Refresh();
    }

    public override void OnCreateRoomFailed(
        short returnCode,
        string message)
    {
        SetTemporaryStatus(
            PhotonFeedbackText.RoomError(
                returnCode
            )
        );

        Refresh();
    }

    public override void OnDisconnected(
       DisconnectCause cause)
    {
        SetTemporaryStatus(
            PhotonFeedbackText.Disconnect(
                cause
            ),
            6f
        );

        Refresh();
    }

    // --------------------------------------------------
    // ACTUALIZAR UI
    // --------------------------------------------------

    private void Refresh()
    {
        SetText(
            titleText,
            "Prop Hunt"
        );

        bool inRoom =
            PhotonNetwork.InRoom;

        bool inLobby =
            PhotonNetwork.InLobby;

        SetActive(
            lobbyPanel,
            !inRoom
        );

        SetActive(
            currentRoomPanel,
            inRoom
        );

        SetText(
            nicknameText,
            $"Jugador: {PhotonNetwork.NickName}"
        );

        if (nicknameInput != null &&
            !nicknameInput.isFocused)
        {
            nicknameInput.SetTextWithoutNotify(
                PhotonNetwork.NickName
            );
        }

        if (connectButton != null)
        {
            connectButton.interactable =
                !PhotonNetwork.IsConnected;
        }

        if (renameButton != null)
        {
            renameButton.interactable =
                inLobby;
        }

        if (nicknameInput != null)
        {
            nicknameInput.interactable =
                inLobby;

            nicknameInput.characterLimit =
                32;
        }

        if (roomNameInput != null)
        {
            roomNameInput.interactable =
                inLobby;
        }

        if (createRoomButton != null)
        {
            createRoomButton.interactable =
                inLobby;
        }

        if (leaveButton != null)
        {
            leaveButton.interactable =
                inRoom;
        }

        if (inLobby)
        {
            RefreshRoomList();
        }

        if (inRoom)
        {
            RefreshCurrentRoom();
        }

        RefreshConnectionStatus();
    }

    private void RefreshRoomList()
    {
        List<RoomInfo> availableRooms =
            new List<RoomInfo>();

        foreach (RoomInfo room in
                 roomManager.Rooms)
        {
            if (room == null)
                continue;

            if (room.RemovedFromList)
                continue;

            availableRooms.Add(
                room
            );
        }

        availableRooms.Sort(
            (a, b) =>
                string.Compare(
                    a.Name,
                    b.Name,
                    StringComparison.OrdinalIgnoreCase
                )
        );

        for (int i = 0;
             i < roomSlots.Length;
             i++)
        {
            RoomSlotUI slot =
                roomSlots[i];

            if (slot == null)
                continue;

            if (i >=
                availableRooms.Count)
            {
                ClearRoomSlot(
                    slot
                );

                continue;
            }

            RoomInfo info =
                availableRooms[i];

            SetupRoomSlot(
                slot,
                info
            );
        }
    }

    private void SetupRoomSlot(
        RoomSlotUI slot,
        RoomInfo info)
    {
        slot.roomName =
            info.Name;

        string state = "";

        if (!info.IsOpen)
        {
            state = " - Cerrada";
        }
        else if (info.MaxPlayers > 0 &&
                 info.PlayerCount >=
                 info.MaxPlayers)
        {
            state = " - Llena";
        }

        SetText(
            slot.label,
            $"{info.Name} - " +
            $"{info.PlayerCount}/" +
            $"{info.MaxPlayers}" +
            state
        );

        if (slot.joinButton == null)
            return;

        bool canEnter =
            PropHuntRoundRules.CanEnterRoom(
                info.IsOpen,
                info.PlayerCount,
                info.MaxPlayers
            );

        slot.joinButton.interactable =
            canEnter;

        slot.joinButton
            .onClick
            .RemoveAllListeners();

        string roomName =
            info.Name;

        slot.joinButton
            .onClick
            .AddListener(
                () => JoinRoom(roomName)
            );
    }

    private void ClearRoomSlot(
        RoomSlotUI slot)
    {
        slot.roomName = null;

        SetText(
            slot.label,
            "Sala disponible"
        );

        if (slot.joinButton == null)
            return;

        slot.joinButton.interactable =
            false;

        slot.joinButton
            .onClick
            .RemoveAllListeners();
    }

    private void RefreshCurrentRoom()
    {
        if (!PhotonNetwork.InRoom)
            return;

        StringBuilder text =
            new StringBuilder();

        text.AppendLine(
            PhotonNetwork.CurrentRoom.Name
        );

        text.AppendLine(
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PropHuntRoundRules.RequiredPlayers} jugadores"
        );

        text.AppendLine();

        foreach (Player player in
                 PhotonNetwork.PlayerList)
        {
            string playerName =
                string.IsNullOrWhiteSpace(
                    player.NickName
                )
                    ? $"Jugador {player.ActorNumber}"
                    : player.NickName;

            text.Append(
                playerName
            );

            if (player.IsMasterClient)
            {
                text.Append(
                    " (Master)"
                );
            }

            text.AppendLine();
        }

        text.AppendLine();

        if (PhotonNetwork.CurrentRoom.PlayerCount <
            PropHuntRoundRules.RequiredPlayers)
        {
            text.Append(
                PhotonFeedbackText.WaitingPlayers(
                    PhotonNetwork.CurrentRoom.PlayerCount
                )
            );
        }
        else
        {
            text.Append(
                "Comenzando partida..."
            );
        }

        SetText(
            roomText,
            text.ToString()
        );
    }

    private void SetTemporaryStatus(
    string message,
    float duration = 4f)
    {
        statusHoldUntil =
            Time.unscaledTime + duration;

        SetStatus(message);
    }

    private void RefreshConnectionStatus()
    {
        // No pisa mensajes de error importantes
        // mientras Photon todavía se está conectando.

        if (Time.unscaledTime <
    statusHoldUntil)
        {
            return;
        }

        if (PhotonNetwork.InRoom)
        {
            SetStatus(
                PhotonFeedbackText.WaitingPlayers(
                    PhotonNetwork.CurrentRoom.PlayerCount
                )
            );

            return;
        }

        if (PhotonNetwork.InLobby)
        {
            SetStatus(
                "Lobby conectado. Elegí o creá una sala."
            );

            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            SetStatus(
                "Conectado a Photon."
            );

            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            SetStatus(
                "Conectando..."
            );
        }
    }

    private void EnsureNickname()
    {
        if (!string.IsNullOrWhiteSpace(
            PhotonNetwork.NickName))
        {
            return;
        }

        PhotonNetwork.NickName =
            $"Jugador{UnityEngine.Random.Range(1000, 9999)}";
    }

    // --------------------------------------------------
    // HELPERS UI
    // --------------------------------------------------

    private void SetStatus(
        string message)
    {
        SetText(
            statusText,
            message
        );
    }

    private static void SetText(
        Text target,
        string value)
    {
        if (target == null)
            return;

        string finalValue =
            value ?? "";

        if (target.text ==
            finalValue)
        {
            return;
        }

        target.supportRichText =
            false;

        target.text =
            finalValue;
    }

    private static void SetActive(
        GameObject target,
        bool value)
    {
        if (target == null)
            return;

        if (target.activeSelf ==
            value)
        {
            return;
        }

        target.SetActive(
            value
        );
    }
}