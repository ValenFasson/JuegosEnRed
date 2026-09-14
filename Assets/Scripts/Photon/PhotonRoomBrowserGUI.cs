using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;

public sealed class PhotonRoomBrowserGUI :
    MonoBehaviour
{
    [Header("Services")]
    [SerializeField]
    private PropHuntRoomManager roomManager;

    [Header("Status")]
    [SerializeField]
    private Text titleText;

    [SerializeField]
    private Text statusText;

    [SerializeField]
    private Button connectButton;

    [Header("Lobby")]
    [SerializeField]
    private GameObject lobbyPanel;

    [SerializeField]
    private Text nicknameText;

    [SerializeField]
    private InputField nicknameInput;

    [SerializeField]
    private Button renameButton;

    [SerializeField]
    private RoomSlot[] roomSlots =
        new RoomSlot[4];

    [Header("Current Room")]
    [SerializeField]
    private GameObject currentRoomPanel;

    [SerializeField]
    private Text roomText;

    [SerializeField]
    private Button leaveButton;

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
                "PhotonRoomBrowserGUI: " +
                "no se encontró PropHuntRoomManager.",
                this
            );

            enabled = false;
            return;
        }

        EnsureNickname();

        ConfigureRoomButtons();

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
            Time.unscaledTime + 0.2f;

        Refresh();
    }

    // --------------------------------------------------
    // CONNECTION
    // --------------------------------------------------

    public void Connect()
    {
        if (roomManager == null)
            return;

        roomManager.Connect();
    }

    // --------------------------------------------------
    // ROOM BUTTONS
    // --------------------------------------------------

    private void ConfigureRoomButtons()
    {
        foreach (RoomSlot slot in roomSlots)
        {
            if (slot == null)
                continue;

            if (slot.joinButton == null)
                continue;

            string roomName =
                slot.roomName;

            slot.joinButton
                .onClick
                .AddListener(
                    () => JoinRoom(roomName)
                );
        }
    }

    public void JoinRoom(
        string roomName)
    {
        if (roomManager == null)
            return;

        roomManager.JoinOrCreateRoom(
            roomName
        );
    }

    public void LeaveRoom()
    {
        if (roomManager == null)
            return;

        roomManager.LeaveRoom();
    }

    // --------------------------------------------------
    // NICKNAME
    // --------------------------------------------------

    public void ChangeNickname()
    {
        if (nicknameInput == null)
            return;

        if (roomManager == null ||
            !roomManager.CanUseLobby)
        {
            return;
        }

        string newName =
            nicknameInput.text
                .Trim()
                .Replace("\n", "")
                .Replace("\r", "");

        if (string.IsNullOrWhiteSpace(newName))
        {
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

    private void EnsureNickname()
    {
        if (!string.IsNullOrWhiteSpace(
            PhotonNetwork.NickName))
        {
            return;
        }

        PhotonNetwork.NickName =
            $"Jugador{Random.Range(1000, 9999)}";

        if (nicknameInput != null)
        {
            nicknameInput.SetTextWithoutNotify(
                PhotonNetwork.NickName
            );
        }
    }

    // --------------------------------------------------
    // REFRESH
    // --------------------------------------------------

    private void Refresh()
    {
        if (roomManager == null)
            return;

        bool inRoom =
            PhotonNetwork.InRoom;

        SetText(
            titleText,
            "Prop Hunt"
        );

        SetText(
            statusText,
            roomManager.StatusMessage
        );

        if (statusText != null)
        {
            statusText.color =
                SeverityColor(
                    roomManager.StatusSeverity
                );
        }

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
            "Jugador: " +
            PhotonNetwork.NickName
        );

        RefreshConnectionControls();

        RefreshRoomSlots();

        if (inRoom)
        {
            RefreshCurrentRoom();
        }
    }

    private void RefreshConnectionControls()
    {
        if (connectButton != null)
        {
            connectButton.interactable =
                !PhotonNetwork.IsConnected;
        }

        bool canUseLobby =
            roomManager.CanUseLobby;

        if (nicknameInput != null)
        {
            nicknameInput.interactable =
                canUseLobby;

            nicknameInput.characterLimit =
                32;
        }

        if (renameButton != null)
        {
            renameButton.interactable =
                canUseLobby;
        }

        if (leaveButton != null)
        {
            leaveButton.interactable =
                PhotonNetwork.InRoom;
        }
    }

    private void RefreshRoomSlots()
    {
        foreach (RoomSlot slot in roomSlots)
        {
            if (slot == null)
                continue;

            if (string.IsNullOrWhiteSpace(
                slot.roomName))
            {
                continue;
            }

            bool known =
                roomManager.TryGetRoomInfo(
                    slot.roomName,
                    out RoomInfo info
                );

            if (!known)
            {
                SetText(
                    slot.label,
                    $"{slot.roomName} - 0/" +
                    $"{PropHuntRoundRules.RequiredPlayers}"
                );

                SetInteractable(
                    slot.joinButton,
                    roomManager.CanUseLobby
                );

                continue;
            }

            string suffix = "";

            if (!info.IsOpen)
            {
                suffix = " - Cerrada";
            }
            else if (
                info.MaxPlayers > 0 &&
                info.PlayerCount >=
                info.MaxPlayers)
            {
                suffix = " - Llena";
            }

            int maxPlayers =
                info.MaxPlayers > 0
                    ? info.MaxPlayers
                    : PropHuntRoundRules
                        .RequiredPlayers;

            SetText(
                slot.label,
                $"{slot.roomName} - " +
                $"{info.PlayerCount}/" +
                $"{maxPlayers}" +
                suffix
            );

            bool canEnter =
                roomManager.CanUseLobby &&
                PropHuntRoundRules.CanEnterRoom(
                    info.IsOpen,
                    info.PlayerCount,
                    info.MaxPlayers
                );

            SetInteractable(
                slot.joinButton,
                canEnter
            );
        }
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

        text.AppendLine();

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
                    player.NickName)
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
                "Iniciando partida..."
            );
        }

        SetText(
            roomText,
            text.ToString()
        );
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------

    private static Color SeverityColor(
        PhotonFeedbackSeverity severity)
    {
        switch (severity)
        {
            case PhotonFeedbackSeverity.Error:
                return new Color(
                    1f,
                    0.35f,
                    0.35f
                );

            case PhotonFeedbackSeverity.Warning:
                return new Color(
                    1f,
                    0.8f,
                    0.3f
                );

            default:
                return Color.white;
        }
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

    private static void SetInteractable(
        Selectable target,
        bool value)
    {
        if (target != null)
        {
            target.interactable =
                value;
        }
    }

    private static void SetActive(
        GameObject target,
        bool value)
    {
        if (target != null &&
            target.activeSelf != value)
        {
            target.SetActive(value);
        }
    }
}