using System;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PhotonRoomBrowserGUI : MonoBehaviour
{
    [Header("Services")]
    [SerializeField] private PhotonRoomBrowser roomBrowser;
    [SerializeField] private PhotonConnectionTestPanel connectionTests;
    [SerializeField] private PhotonCanvasPanels panelNavigation;
    [SerializeField] private EventSystem eventSystem;

    [Header("Status and Notices")]
    [SerializeField] private Text titleText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text noticesText;
    [SerializeField] private ScrollRect noticesScroll;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button connectionTestsButton;

    [Header("Lobby")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private Text nicknameText;
    [SerializeField] private InputField nicknameInput;
    [SerializeField] private Button renameButton;
    [SerializeField] private RoomSlot[] roomSlots = new RoomSlot[0];

    [Header("Current Room")]
    [SerializeField] private GameObject currentRoomPanel;
    [SerializeField] private Text roomText;
    [SerializeField] private Text roleText;
    [SerializeField] private Text roundText;
    [SerializeField] private Text objectivesText;
    [SerializeField] private Text instructionText;
    [SerializeField] private Text recoveryText;
    [SerializeField] private Text resultText;
    [SerializeField] private Text configurationText;
    [SerializeField] private Button nextRoundButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private ScrollRect mainScroll;

    [Header("Diagnostics")]
    [SerializeField] private Toggle diagnosticsToggle;
    [SerializeField] private GameObject diagnosticsPanel;
    [SerializeField] private Text diagnosticsText;

    private double nextRefresh;
    private bool originalNavigation;
    private bool previousInRoom;

    private void OnEnable()
    {
        if (roomBrowser == null)
        {
            Debug.LogError("Canvas: assign PhotonRoomBrowser in the Inspector.", this);
            enabled = false;
            return;
        }
        if (HidesController(lobbyPanel) || HidesController(currentRoomPanel) || HidesController(diagnosticsPanel))
        {
            Debug.LogError("Canvas: the panels must be children of the object that contains the controllers.", this);
            enabled = false;
            return;
        }
        if (eventSystem != null)
            originalNavigation = eventSystem.sendNavigationEvents;
        roomBrowser.StatusChanged += Refresh;
        roomBrowser.FeedbackChanged += RefreshNotices;
        previousInRoom = PhotonNetwork.InRoom;
        nextRefresh = 0d;
        RefreshNotices();
        Refresh();
    }

    private bool HidesController(GameObject panel) => panel != null && transform.IsChildOf(panel.transform);

    private void OnDisable()
    {
        if (roomBrowser != null)
        {
            roomBrowser.StatusChanged -= Refresh;
            roomBrowser.FeedbackChanged -= RefreshNotices;
        }
        if (eventSystem != null)
            eventSystem.sendNavigationEvents = originalNavigation;
    }

    private void Update()
    {
        // Counts, confirmed game properties and recovery timers have independent lifetimes.
        if (Time.realtimeSinceStartupAsDouble >= nextRefresh)
        {
            nextRefresh = Time.realtimeSinceStartupAsDouble + 0.2d;
            Refresh();
        }
        if (eventSystem != null)
        {
            bool testsOpen = connectionTests != null && connectionTests.Visible;
            bool typing = nicknameInput != null && nicknameInput.isFocused;
            bool navigation = testsOpen || typing || !PhotonNetwork.InRoom || PropHuntGame.Phase == GamePhase.Finished;
            if (!navigation && eventSystem.sendNavigationEvents)
                eventSystem.SetSelectedGameObject(null);
            eventSystem.sendNavigationEvents = navigation;
        }
    }

    public void Connect() => roomBrowser?.Connect();
    public void JoinRoom(string roomName) => roomBrowser?.JoinOrCreateRoom(roomName);
    public void LeaveRoom() => roomBrowser?.LeaveCurrentRoom();
    public void PrepareNextRound() => PropHuntGame.Instance?.PrepareNextRound();

    public void ToggleConnectionTests()
    {
        if (panelNavigation != null)
            panelNavigation.ToggleConnectionTests();
        else if (connectionTests != null)
            connectionTests.TogglePanel();
    }

    public void ChangeNickname()
    {
        if (nicknameInput == null || roomBrowser == null || !roomBrowser.CanUseLobby)
            return;
        string name = nicknameInput.text.Trim().Replace("\r", "").Replace("\n", "");
        if (name.Length == 0)
        {
            roomBrowser.Notify("Escribí un nombre para tu jugador.", PhotonFeedbackSeverity.Warning);
            return;
        }
        PhotonNetwork.NickName = name.Length > 32 ? name.Substring(0, 32) : name;
        nicknameInput.SetTextWithoutNotify(PhotonNetwork.NickName);
        Refresh();
    }

    private void Refresh()
    {
        if (roomBrowser == null)
            return;
        bool inRoom = PhotonNetwork.InRoom;
        SetText(titleText, "Prop Hunt");
        SetText(statusText, roomBrowser.StatusMessage);
        if (statusText != null)
            statusText.color = SeverityColor(roomBrowser.StatusSeverity);
        SetActive(lobbyPanel, !inRoom);
        SetActive(currentRoomPanel, inRoom);
        SetText(nicknameText, "Jugador: " + PhotonNetwork.NickName);
        SetInteractable(renameButton, roomBrowser.CanUseLobby);
        if (nicknameInput != null)
        {
            nicknameInput.interactable = roomBrowser.CanUseLobby;
            nicknameInput.characterLimit = 32;
        }
        SetInteractable(connectButton, !inRoom && !roomBrowser.IsRecovering && roomBrowser.ProcessState == PhotonProcessState.Disconnected);
        SetInteractable(connectionTestsButton, connectionTests != null && connectionTests.isActiveAndEnabled && PhotonConnectionTestPanel.Available);

        foreach (RoomSlot slot in roomSlots)
        {
            if (slot == null)
                continue;
            bool known = roomBrowser.TryGetRoomInfo(slot.roomName ?? "", out RoomInfo info);
            bool allowedName = false;
            foreach (string name in roomBrowser.RoomNames)
                allowedName |= string.Equals(name, slot.roomName, StringComparison.OrdinalIgnoreCase);
            bool enter = allowedName && roomBrowser.CanUseLobby && (!known ||
                PropHuntRoundRules.CanEnterRoom(info.IsOpen, info.PlayerCount, info.MaxPlayers));
            SetText(slot.label, known ? $"{slot.roomName} — {info.PlayerCount}/{info.MaxPlayers}" + (!info.IsOpen ? " — Cerrada" : info.PlayerCount >= info.MaxPlayers ? " — Llena" : "") : $"{slot.roomName} — 0/{roomBrowser.MaxPlayersPerRoom}");
            SetInteractable(slot.joinButton, enter);
        }

        SetInteractable(leaveButton, inRoom && roomBrowser.ProcessState == PhotonProcessState.InRoom);
        SetInteractable(nextRoundButton, inRoom && PhotonNetwork.IsMasterClient && PropHuntGame.Phase == GamePhase.Finished);
        SetText(configurationText, PropHuntGame.Instance != null ? PropHuntGame.Instance.ConfigurationError : "");
        SetText(recoveryText, RecoveryText());
        if (inRoom)
            RefreshCurrentRoom();
        if (mainScroll != null && previousInRoom != inRoom)
            mainScroll.verticalNormalizedPosition = 1f;
        previousInRoom = inRoom;
        bool diagnostics = diagnosticsToggle != null && diagnosticsToggle.isOn;
        SetActive(diagnosticsPanel, diagnostics);
        if (diagnostics)
            SetText(diagnosticsText, PhotonLocalValidation.DescribeState());
    }

    private void RefreshCurrentRoom()
    {
        var players = new StringBuilder();
        players.AppendLine($"{PhotonNetwork.CurrentRoom.Name} — {PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}");
        foreach (Player player in PhotonNetwork.PlayerList)
            players.AppendLine(roomBrowser.PlayerName(player.ActorNumber) + (player.IsInactive ? " (sin conexión)" : "") + (player.IsMasterClient ? " (Master)" : ""));
        SetText(roomText, players.ToString());
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        bool participant = PropHuntGame.IsParticipant(actor);
        bool hunter = PropHuntGame.TryGetShooterActorNumber(out int hunterActor) && actor == hunterActor;
        SetText(roleText, participant ? "Rol: " + (hunter ? "Cazador" : "Prop") + " — " + PropHuntGame.GetPlayerState(actor) : "Esperando la próxima ronda.");
        SetText(roundText, $"Ronda {PropHuntGame.CurrentRound} — {PhotonFeedbackText.Phase(PropHuntGame.Phase)}");
        SetText(objectivesText, $"Botones activados: {PropHuntGame.ActivatedButtonCount}/2 | Props vivos: {PropHuntGame.AlivePropCount}");
        SetText(instructionText, PropHuntGame.CanLocalPlayerActivateButtons() ? "W/S o flechas: mover. A/D: girar. E: activar un botón cercano." : PropHuntGame.CanLocalPlayerShoot() ? "W/S o flechas: mover. A/D: girar. Espacio: disparar." : "");
        SetText(resultText, PropHuntGame.Phase == GamePhase.Finished ? PhotonFeedbackText.Result(PropHuntGame.WinnerTeam, PropHuntGame.EndReason) : "");
    }

    private string RecoveryText()
    {
        if (roomBrowser.IsRecovering)
            return $"Recuperando '{roomBrowser.RecoveryRoom}' — intento {roomBrowser.ReconnectAttempts}.";
        if (!PhotonNetwork.InRoom)
            return "";
        var text = new StringBuilder();
        foreach (int actor in PropHuntGame.Participants())
        {
            double remaining = PropHuntGame.ReconnectDeadline(actor) - PhotonNetwork.Time;
            if (remaining > 0d)
                text.AppendLine($"{roomBrowser.PlayerName(actor)}: puede regresar durante {remaining:F0} s.");
        }
        return text.ToString();
    }

    private void RefreshNotices()
    {
        if (roomBrowser == null || noticesText == null)
            return;
        var text = new StringBuilder();
        foreach (PhotonFeedbackNotice notice in roomBrowser.Notices)
            text.AppendLine((notice.Severity == PhotonFeedbackSeverity.Error ? "Error: " : notice.Severity == PhotonFeedbackSeverity.Warning ? "Aviso: " : "") + notice.Message);
        noticesText.supportRichText = false;
        SetText(noticesText, text.ToString());
        if (noticesScroll != null)
            noticesScroll.verticalNormalizedPosition = 0f;
    }

    private static Color SeverityColor(PhotonFeedbackSeverity severity) => severity == PhotonFeedbackSeverity.Error ? new Color(1f, 0.35f, 0.35f) : severity == PhotonFeedbackSeverity.Warning ? new Color(1f, 0.8f, 0.3f) : Color.white;

    private static void SetText(Text target, string value)
    {
        if (target != null && target.text != (value ?? ""))
        {
            target.supportRichText = false;
            target.text = value ?? "";
        }
    }
    private static void SetInteractable(Selectable target, bool value)
    {
        if (target != null)
            target.interactable = value;
    }
    private static void SetActive(GameObject target, bool value)
    {
        if (target != null && target.activeSelf != value)
            target.SetActive(value);
    }
}
