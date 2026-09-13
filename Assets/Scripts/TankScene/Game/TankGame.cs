using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class TankGame : MonoBehaviourPunCallbacks
{
    public const string ReadyRoundKey = "readyRound";
    public const string ReadyTokenKey = "readyToken";
    internal const string ProtocolKey = "syncProtocol";
    private const string RoundKey = "roundId";
    private const string RevisionKey = "revision";
    private const string PhaseKey = "phase";
    private const string ResumePhaseKey = "resumePhase";
    private const string ParticipantsKey = "participants";
    private const string SpawnDeadlineKey = "spawnDeadline";
    private const string ResultUntilKey = "resultUntil";
    private const string WinnerTeamKey = "winnerTeam";
    private const string EndReasonKey = "endReason";
    internal const string ShooterActorKey = "shooterActor";
    private const string ActivatedButtonsKey = "activatedButtons";

    [Header("Player")]
    [SerializeField] private GameObject tankPrefab;
    [SerializeField] private Transform spawnPoint;

    private TankPlayerLifecycle players;
    private TankShotSystem shots;
    private bool initializationRequested;
    private bool mutationPending;
    private int pendingRevision;
    private float mutationSentAt;
    private float nextAuthorityTick;
    private float nextPoseCheckpoint;
    private float authorityResumeAt;

    public static TankGame Instance { get; private set; }
    public string LastNetworkMessage { get; private set; }
    public string ConfigurationError { get; private set; }
    public static int CurrentRound => ReadInt(RoundKey);
    public static GamePhase Phase => (GamePhase)ReadInt(PhaseKey);
    public static string WinnerTeam => ReadString(WinnerTeamKey);
    public static string EndReason => ReadString(EndReasonKey);
    public static int AlivePropCount => CountAliveProps(null);
    public static bool IsButtonActivated(int buttonId) =>
        buttonId >= 0 && buttonId < PropHuntRoundRules.RequiredButtons &&
        (ReadInt(ActivatedButtonsKey) & (1 << buttonId)) != 0;
    public static int ActivatedButtonCount =>
        (IsButtonActivated(0) ? 1 : 0) + (IsButtonActivated(1) ? 1 : 0);

    public static bool CanLocalPlayerActivateButtons() => CanLocalPlayerMove() &&
        TryGetShooterActorNumber(out int hunter) && hunter != PhotonNetwork.LocalPlayer.ActorNumber;

    internal static Hashtable Properties =>
        PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.CustomProperties : null;

    internal static string StateKey(int actor) => "state_" + actor;
    internal static string SlotKey(int actor) => "slot_" + actor;
    internal static string PoseKey(int actor) => "pose_" + actor;
    internal static string TokenKey(int actor) => "token_" + actor;
    internal static string ReconnectKey(int actor) => "reconnectUntil_" + actor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }
        Instance = this;
        players = new TankPlayerLifecycle(tankPrefab, spawnPoint);
        shots = new TankShotSystem(this);
        ConfigurationError = ValidateConfiguration();
        if (ConfigurationError != null)
            Debug.LogError(ConfigurationError, this);
    }

    private string ValidateConfiguration()
    {
        if (spawnPoint == null)
            return "TankGame necesita un spawnPoint.";
        if (tankPrefab == null)
            return "TankGame necesita un prefab de Tank.";
        TankController controller = tankPrefab.GetComponent<TankController>();
        if (controller == null || tankPrefab.GetComponent<TankHealth>() == null ||
            tankPrefab.GetComponent<PhotonView>() == null)
            return "El Tank necesita TankController, TankHealth y PhotonView.";
        if (Resources.Load<GameObject>(tankPrefab.name) == null)
            return "El prefab de Tank debe estar disponible en Resources.";
        if (!controller.TryGetProjectileConfiguration(out TankProjectile projectile) ||
            Resources.Load<GameObject>(projectile.gameObject.name) == null)
            return "El proyectil necesita TankProjectile, PhotonView y un prefab en Resources.";
        return null;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        PhotonNetwork.NetworkingClient.OpResponseReceived += OnOperationResponse;
    }

    public override void OnDisable()
    {
        PhotonNetwork.NetworkingClient.OpResponseReceived -= OnOperationResponse;
        base.OnDisable();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        // Este proyecto usa una sola escena.
        PhotonNetwork.AutomaticallySyncScene = false;

        if (PhotonNetwork.InRoom)
            HandleJoinedRoom();
    }

    public override void OnJoinedRoom()
    {
        HandleJoinedRoom();
    }

    private void HandleJoinedRoom()
    {
        ResetLocalState();
        players.JoinedRoom();
        EnsureRoomState();
        players.UpdateLocalPlayer();
    }

    private void EnsureRoomState()
    {
        if (!PhotonNetwork.IsMasterClient || initializationRequested || Properties.ContainsKey(ProtocolKey))
            return;
        // CAS cannot initialize keys. Initialize once, then use revision CAS for every transition.
        initializationRequested = PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { ProtocolKey, PropHuntRoundRules.Protocol }, { RevisionKey, 0 },
            { RoundKey, 0 }, { PhaseKey, (int)GamePhase.Waiting },
            { ParticipantsKey, Array.Empty<int>() }, { ShooterActorKey, 0 },
            { WinnerTeamKey, "" }, { EndReasonKey, "" },
            { TankShotSystem.LastShotKey, 0 }, { TankShotSystem.NextFireKey, 0d }, { ActivatedButtonsKey, 0 }
        });
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        nextAuthorityTick = 0f;
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        mutationPending = false;
        initializationRequested = false;
        shots.Reset();
        // Give already-sent instantiate events a chance to arrive before repairing a shot journal.
        authorityResumeAt = Time.realtimeSinceStartup + 0.5f;
        EnsureRoomState();
    }

    private void Update()
    {
        if (!PhotonNetwork.InRoom || ConfigurationError != null)
            return;
        players.UpdateLocalPlayer();
        if (CanTickAuthority())
            TickRound();
    }

    private bool CanTickAuthority()
    {
        if (!PhotonNetwork.IsMasterClient || Time.realtimeSinceStartup < authorityResumeAt)
            return false;
        if (mutationPending && Time.realtimeSinceStartup - mutationSentAt > 5f)
            mutationPending = false;
        if (mutationPending || Time.realtimeSinceStartup < nextAuthorityTick)
            return false;
        nextAuthorityTick = Time.realtimeSinceStartup + 0.1f;
        EnsureRoomState();
        return ReadInt(ProtocolKey) == PropHuntRoundRules.Protocol;
    }

    private void TickRound()
    {
        // Room admission and current-round participation are separate.
        if (!PhotonNetwork.CurrentRoom.IsOpen)
            PhotonNetwork.CurrentRoom.IsOpen = true;
        if (Phase != GamePhase.Waiting && Phase != GamePhase.Finished && ReconcileConnections())
            return;
        switch (Phase)
        {
            case GamePhase.Waiting:
                TryStartGame();
                break;
            case GamePhase.Spawning:
                TryFinishSpawning();
                break;
            case GamePhase.Playing:
                shots.ReconcileProjectiles();
                SavePoseCheckpoint();
                break;
            case GamePhase.Finished:
                if (WinnerTeam == "None" && PhotonNetwork.Time >= ReadDouble(ResultUntilKey))
                    PrepareNextRound();
                break;
        }
    }

    private void SavePoseCheckpoint()
    {
        if (mutationPending || Time.realtimeSinceStartup < nextPoseCheckpoint)
            return;
        nextPoseCheckpoint = Time.realtimeSinceStartup + 1f;
        Hashtable checkpoint = new Hashtable();
        TankPlayerLifecycle.CapturePoses(checkpoint);
        if (checkpoint.Count > 0)
            Commit(checkpoint);
    }

    private void TryStartGame()
    {
        List<Player> active = new List<Player>();
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (!player.IsInactive)
                active.Add(player);
        }
        if (active.Count != PropHuntRoundRules.RequiredPlayers)
            return;
        active.Sort((a, b) => a.ActorNumber.CompareTo(b.ActorNumber));
        int[] participants = new int[active.Count];
        Hashtable changes = new Hashtable
        {
            { RoundKey, CurrentRound + 1 }, { PhaseKey, (int)GamePhase.Spawning },
            { ShooterActorKey, active[UnityEngine.Random.Range(0, active.Count)].ActorNumber },
            { SpawnDeadlineKey, PhotonNetwork.Time + PropHuntRoundRules.PreparationTimeoutSeconds },
            { WinnerTeamKey, "" }, { EndReasonKey, "" },
            { TankShotSystem.LastShotKey, 0 }, { TankShotSystem.NextFireKey, 0d }, { ActivatedButtonsKey, 0 }
        };
        for (int i = 0; i < active.Count; i++)
        {
            int actor = active[i].ActorNumber;
            participants[i] = actor;
            changes[StateKey(actor)] = (int)PropHuntPlayerState.Alive;
            changes[SlotKey(actor)] = i;
            changes[TokenKey(actor)] = 0;
            changes[ReconnectKey(actor)] = 0d;
            changes[PoseKey(actor)] = players.SpawnPose(i);
        }
        changes[ParticipantsKey] = participants;
        Commit(changes);
    }

    private void TryFinishSpawning()
    {
        if (TankPlayerLifecycle.ReadyToPlay())
            Commit(new Hashtable { { PhaseKey, (int)GamePhase.Playing } });
        else if (PropHuntRoundRules.GraceExpired(PhotonNetwork.Time, ReadDouble(SpawnDeadlineKey)))
            Commit(FinishChanges("None", "SpawnTimeout"));
    }

    public override void OnRoomPropertiesUpdate(
        Hashtable propertiesThatChanged)
    {
        if (mutationPending && ReadInt(RevisionKey) != pendingRevision)
            mutationPending = false;
        shots.OnPropertiesChanged(propertiesThatChanged);
        players.UpdateLocalPlayer();
        UpdateNetworkMessage(propertiesThatChanged);
    }

    public static int ReadInt(string key, int fallback = 0)
    {
        return Properties != null && Properties[key] is int value ? value : fallback;
    }

    public static double ReadDouble(string key)
    {
        return Properties != null && Properties[key] is double value ? value : 0d;
    }

    private static string ReadString(string key) => Properties != null && Properties[key] is string value ? value : "";

    internal static int[] Participants() => Properties != null && Properties[ParticipantsKey] is int[] value ? value : Array.Empty<int>();

    public static PropHuntPlayerState GetPlayerState(int actor) => (PropHuntPlayerState)ReadInt(StateKey(actor), (int)PropHuntPlayerState.Abandoned);

    public static bool IsParticipant(int actor) => Array.IndexOf(Participants(), actor) >= 0;
    public static int RecoveryToken(int actor) => ReadInt(TokenKey(actor));
    public static double ReconnectDeadline(int actor) => ReadDouble(ReconnectKey(actor));
    public static int LastAcceptedShot => ReadInt(TankShotSystem.LastShotKey);

    public static bool TryGetPose(int actor, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (Properties == null || !(Properties[PoseKey(actor)] is object[] data) ||
            data.Length != 2 || !(data[0] is Vector3 p) || !(data[1] is Quaternion r))
            return false;
        position = p;
        rotation = r;
        return true;
    }

    public static bool TryGetShooterActorNumber(out int actorNumber)
    {
        actorNumber = ReadInt(ShooterActorKey);
        return actorNumber > 0 && IsParticipant(actorNumber);
    }

    public static bool CanLocalPlayerShoot()
    {
        return CanLocalPlayerMove() && TryGetShooterActorNumber(out int shooterActorNumber) &&
               shooterActorNumber == PhotonNetwork.LocalPlayer.ActorNumber;
    }

    public static bool CanLocalPlayerMove()
    {
        return PhotonNetwork.InRoom && Phase == GamePhase.Playing &&
               GetPlayerState(PhotonNetwork.LocalPlayer.ActorNumber) == PropHuntPlayerState.Alive &&
               ReconnectDeadline(PhotonNetwork.LocalPlayer.ActorNumber) == 0d &&
               IsReady(PhotonNetwork.LocalPlayer.ActorNumber);
    }

    internal static bool IsReady(int actor) => TankPlayerLifecycle.IsReady(actor);

    private bool ReconcileConnections()
    {
        Hashtable changes = new Hashtable();
        if (TankPlayerLifecycle.CollectConnectionChanges(changes, out bool mustPause))
            return Commit(FinishChanges("None", "HunterDisconnected"));
        if (CountAliveProps(changes) == 0)
        {
            AddFinishChanges(changes, "Hunter", "CapturedAllOrAbandoned");
            return Commit(changes);
        }
        if (mustPause && Phase != GamePhase.Paused)
        {
            changes[ResumePhaseKey] = (int)Phase;
            changes[PhaseKey] = (int)GamePhase.Paused;
            TankPlayerLifecycle.CapturePoses(changes);
            // Cancel shots only for hunter recovery. Prop outages leave the round running.
            TankShotSystem.ClearShotProperties(changes);
        }
        else if (!mustPause && Phase == GamePhase.Paused)
        {
            GamePhase resume = (GamePhase)ReadInt(ResumePhaseKey, (int)GamePhase.Playing);
            changes[PhaseKey] = (int)resume;
            if (resume == GamePhase.Spawning)
                changes[SpawnDeadlineKey] = PhotonNetwork.Time + PropHuntRoundRules.PreparationTimeoutSeconds;
        }
        return changes.Count > 0 && Commit(changes);
    }

    private static int CountAliveProps(Hashtable changes)
    {
        int count = 0;
        int hunter = ReadInt(ShooterActorKey);
        foreach (int actor in Participants())
        {
            PropHuntPlayerState state = changes != null && changes[StateKey(actor)] is int changed
                ? (PropHuntPlayerState)changed : GetPlayerState(actor);
            if (actor != hunter && state == PropHuntPlayerState.Alive)
                count++;
        }
        return count;
    }

    internal static bool IsHunterAvailableForGameplay()
    {
        if (!PhotonNetwork.InRoom || !TryGetShooterActorNumber(out int hunter))
            return false;
        Player player = PhotonNetwork.CurrentRoom.GetPlayer(hunter);
        return GetPlayerState(hunter) == PropHuntPlayerState.Alive &&
            PropHuntRoundRules.HunterAllowsGameplay(player != null,
                player != null && player.IsInactive, IsReady(hunter), ReconnectDeadline(hunter));
    }

    public bool TryActivateButton(TankController source, Player sender, int round, int buttonId)
    {
        if (!CanProcessPlayerAction(source, sender, round))
            return false;
        PropHuntObjectiveButton button = PropHuntObjectiveButton.Find(buttonId);
        if (button == null)
            return false;
        // Block hunter outages before the pause is confirmed, without blocking prop outages.
        if (!IsHunterAvailableForGameplay() || ReconnectDeadline(sender.ActorNumber) > 0d)
            return false;
        int mask = ReadInt(ActivatedButtonsKey);
        if (!PropHuntRoundRules.CanActivateButton(Phase, sender.ActorNumber, ReadInt(ShooterActorKey),
            IsParticipant(sender.ActorNumber), GetPlayerState(sender.ActorNumber),
            !sender.IsInactive && IsReady(sender.ActorNumber), round, CurrentRound, buttonId, mask,
            (source.transform.position - button.transform.position).sqrMagnitude))
            return false;
        mask |= 1 << buttonId;
        Hashtable changes = new Hashtable { { ActivatedButtonsKey, mask } };
        // Objective completion and the result share the same revision CAS as eliminations.
        if (PropHuntRoundRules.AllButtonsActivated(mask))
            AddFinishChanges(changes, "Props", "ButtonsActivated");
        return Commit(changes);
    }

    // Public facade preserves existing callers; the shot system owns the implementation.
    public bool TryAcceptShot(TankController source, Player sender, int round, int sequence, Vector3 direction) =>
        shots.TryAcceptShot(source, sender, round, sequence, direction);

    public static bool TryGetShot(int sequence, out object[] data) => TankShotSystem.TryGetShot(sequence, out data);
    public static bool IsShotActive(int round, int sequence) => TankShotSystem.IsShotActive(round, sequence);

    internal bool CanProcessPlayerAction(TankController source, Player sender, int round) =>
        PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && ConfigurationError == null &&
        !mutationPending && Time.realtimeSinceStartup >= authorityResumeAt &&
        source != null && sender != null && source.photonView.OwnerActorNr == sender.ActorNumber &&
        source.ActorNumber == sender.ActorNumber && source.RoundId == round && round == CurrentRound &&
        TankController.FindForActor(sender.ActorNumber, CurrentRound) == source;

    public bool TryResolveImpact(TankProjectile projectile, int targetActor)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || projectile == null ||
            !projectile.photonView.IsRoomView ||
            TankProjectile.FindShot(CurrentRound, projectile.ShotSequence) != projectile ||
            !IsShotActive(projectile.RoundId, projectile.ShotSequence))
            return false;
        if (!IsHunterAvailableForGameplay())
            return false;
        Hashtable changes = TankShotSystem.ConsumeShot(projectile.ShotSequence);
        if (targetActor > 0)
        {
            if (targetActor == ReadInt(ShooterActorKey))
                return false;
            // A disconnected prop keeps its stationary avatar and can still be eliminated.
            if (IsParticipant(targetActor) && PropHuntRoundRules.CanEliminate(Phase,
                projectile.RoundId, CurrentRound, targetActor, ReadInt(ShooterActorKey),
                GetPlayerState(targetActor), true))
                changes[StateKey(targetActor)] = (int)PropHuntPlayerState.Eliminated;
        }
        if (CountAliveProps(changes) == 0)
            AddFinishChanges(changes, "Hunter", "CapturedAll");
        return Commit(changes);
    }

    private static Hashtable FinishChanges(string team, string reason)
    {
        Hashtable changes = new Hashtable();
        AddFinishChanges(changes, team, reason);
        return changes;
    }

    private static void AddFinishChanges(Hashtable changes, string team, string reason)
    {
        changes[PhaseKey] = (int)GamePhase.Finished;
        changes[WinnerTeamKey] = team;
        changes[EndReasonKey] = reason;
        changes[ResultUntilKey] = PhotonNetwork.Time + 3d;
        TankShotSystem.ClearShotProperties(changes);
    }

    public void PrepareNextRound()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || Phase != GamePhase.Finished)
            return;
        Hashtable changes = new Hashtable
        {
            { PhaseKey, (int)GamePhase.Waiting }, { ParticipantsKey, Array.Empty<int>() },
            { ShooterActorKey, 0 }, { WinnerTeamKey, "" }, { EndReasonKey, "" },
            { TankShotSystem.LastShotKey, 0 }, { TankShotSystem.NextFireKey, 0d }, { ActivatedButtonsKey, 0 }
        };
        foreach (int actor in Participants())
        {
            changes[StateKey(actor)] = null;
            changes[SlotKey(actor)] = null;
            changes[TokenKey(actor)] = null;
            changes[ReconnectKey(actor)] = null;
            changes[PoseKey(actor)] = null;
        }
        // Owners clean up their avatars only after this CAS transition is confirmed.
        Commit(changes);
    }

    internal bool Commit(Hashtable changes)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || mutationPending ||
            ReadInt(ProtocolKey) != PropHuntRoundRules.Protocol)
            return false;
        int revision = ReadInt(RevisionKey);
        changes[RevisionKey] = revision + 1;
        Hashtable expected = new Hashtable
        {
            { RevisionKey, revision }, { RoundKey, CurrentRound }, { PhaseKey, (int)Phase }
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(changes, expected))
            return false;
        mutationPending = true;
        pendingRevision = revision;
        mutationSentAt = Time.realtimeSinceStartup;
        return true;
    }

    private void OnOperationResponse(OperationResponse response)
    {
        if (response.OperationCode == OperationCode.SetProperties && response.ReturnCode != 0)
            mutationPending = false;
    }

    private void UpdateNetworkMessage(Hashtable changed)
    {
        // PhotonRoomBrowser owns visible notices. Keep the existing fallback message
        // locally, derived from confirmed state instead of a second network event.
        string message = null;
        foreach (int actor in Participants())
            if (changed[StateKey(actor)] is int state && state == (int)PropHuntPlayerState.Eliminated)
                message = $"Prop {actor} eliminado.";
        if (changed.ContainsKey(PhaseKey))
            message = PhotonFeedbackText.Phase(Phase) + ".";
        if (changed.ContainsKey(WinnerTeamKey) && Phase == GamePhase.Finished)
            message = PhotonFeedbackText.Result(WinnerTeam, EndReason);
        if (message == null)
            return;
        LastNetworkMessage = message;
        Debug.Log("[PropHunt state] " + message);
    }

    private void ResetLocalState(bool disconnected = false)
    {
        players.Reset(disconnected);
        mutationPending = false;
        initializationRequested = false;
        nextAuthorityTick = 0f;
        nextPoseCheckpoint = 0f;
        authorityResumeAt = Time.realtimeSinceStartup + 0.5f;
        shots.Reset();
        LastNetworkMessage = null;
    }

    public override void OnPlayerPropertiesUpdate(
        Player targetPlayer,
        Hashtable changedProps)
    {
        nextAuthorityTick = 0f;
    }

    public override void OnPlayerLeftRoom(
        Player otherPlayer)
    {
        nextAuthorityTick = 0f;
    }

    public override void OnLeftRoom()
    {
        ResetLocalState();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        ResetLocalState(disconnected: true);
    }
}
