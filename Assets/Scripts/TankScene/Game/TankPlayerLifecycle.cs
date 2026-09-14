using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using static PropHuntGame;

// Owns the local avatar and player recovery; TankGame decides round transitions.
internal sealed class TankPlayerLifecycle
{
    private readonly GameObject tankPrefab;
    private readonly Transform spawnPoint;
    private int spawnRequestedRound = -1;
    private int appliedToken = -1;
    private int rejoinedRound = -1;
    private bool restoreLocalPose;
    private float nextReadyRequest;

    internal TankPlayerLifecycle(GameObject tankPrefab, Transform spawnPoint)
    {
        this.tankPrefab = tankPrefab;
        this.spawnPoint = spawnPoint;
    }

    private static readonly Vector3[] SpawnOffsets =
    {
        new Vector3(-6f, 1f, -6f),
        new Vector3(6f, 1f, -6f),
        new Vector3(0f, 1f, 6f)
    };

    private static readonly float[] SpawnRotations =
    {
        45f,
        -45f,
        180f
    };

    internal object[] SpawnPose(int slot) => new object[]
    {
        spawnPoint.position + SpawnOffsets[slot],
        Quaternion.Euler(0f, SpawnRotations[slot], 0f)
    };

    internal void JoinedRoom()
    {
        restoreLocalPose = PhotonNetwork.LocalPlayer.HasRejoined;
        rejoinedRound = restoreLocalPose ? CurrentRound : -1;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { ReadyRoundKey, 0 }, { ReadyTokenKey, -1 }
        });
    }

    internal void Reset(bool disconnected = false)
    {
        spawnRequestedRound = -1;
        appliedToken = -1;
        rejoinedRound = -1;
        nextReadyRequest = 0f;
        restoreLocalPose = disconnected;
    }

    internal static bool IsReady(int actor)
    {
        Player player = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
        return player != null && !player.IsInactive &&
               player.CustomProperties[ReadyRoundKey] is int round && round == CurrentRound &&
               player.CustomProperties[ReadyTokenKey] is int token && token == RecoveryToken(actor) &&
               TankController.FindForActor(actor, CurrentRound) != null;
    }

    internal static bool ReadyToPlay()
    {
        foreach (int actor in Participants())
        {
            Player player = PhotonNetwork.CurrentRoom.GetPlayer(actor);
            // Inactive props must not block preparation for connected players.
            if (GetPlayerState(actor) == PropHuntPlayerState.Alive &&
                player != null && !player.IsInactive && !IsReady(actor))
                return false;
        }
        return true;
    }

    internal void UpdateLocalPlayer()
    {
        if (!PhotonNetwork.InRoom || ReadInt(ProtocolKey) != PropHuntRoundRules.Protocol ||
            PropHuntGame.Instance == null || PropHuntGame.Instance.ConfigurationError != null ||
            Phase == GamePhase.Waiting || Phase == GamePhase.Finished)
            return;
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (!IsParticipant(actor) || GetPlayerState(actor) != PropHuntPlayerState.Alive)
            return;
        TankController tank = TankController.FindForActor(actor, CurrentRound);
        if (tank == null)
        {
            RequestLocalTank(actor);
            return;
        }
        RestoreLocalPose(tank, actor);
        ConfirmReadiness(actor);
    }

    private void RequestLocalTank(int actor)
    {
        // A rejoin must reuse cached network objects, never create a second avatar.
        if (spawnRequestedRound == CurrentRound || rejoinedRound == CurrentRound)
            return;
        int slot = ReadInt(SlotKey(actor), -1);
        if (slot < 0 || slot >= SpawnOffsets.Length)
            return;
        spawnRequestedRound = CurrentRound;
        PhotonNetwork.Instantiate(tankPrefab.name,
            spawnPoint.position + SpawnOffsets[slot],
            Quaternion.Euler(0f, SpawnRotations[slot], 0f), 0,
            new object[] { PropHuntRoundRules.Protocol, CurrentRound, actor });
    }

    private void RestoreLocalPose(TankController tank, int actor)
    {
        int token = RecoveryToken(actor);
        if (restoreLocalPose || appliedToken != token)
        {
            if (TryGetPose(actor, out Vector3 position, out Quaternion rotation))
                tank.RestorePose(position, rotation);
            restoreLocalPose = false;
            appliedToken = token;
        }
    }

    private void ConfirmReadiness(int actor)
    {
        int token = RecoveryToken(actor);
        foreach (int participant in Participants())
        {
            Player player = PhotonNetwork.CurrentRoom.GetPlayer(participant);
            if (GetPlayerState(participant) == PropHuntPlayerState.Alive &&
                player != null && !player.IsInactive &&
                TankController.FindForActor(participant, CurrentRound) == null)
                return;
        }
        Hashtable local = PhotonNetwork.LocalPlayer.CustomProperties;
        if (!(local[ReadyRoundKey] is int readyRound) || readyRound != CurrentRound ||
            !(local[ReadyTokenKey] is int readyToken) || readyToken != token)
        {
            // Avoid per-frame operations while waiting for the property acknowledgement.
            if (Time.realtimeSinceStartup < nextReadyRequest)
                return;
            nextReadyRequest = Time.realtimeSinceStartup + 1f;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { ReadyRoundKey, CurrentRound }, { ReadyTokenKey, token }
            });
        }
    }

    // Returns true if the hunter abandoned. The caller discards partial changes
    // and cancels the round, matching the existing recovery policy.
    internal static bool CollectConnectionChanges(Hashtable changes, out bool mustPause)
    {
        mustPause = false;
        int hunter = ReadInt(ShooterActorKey);
        foreach (int actor in Participants())
        {
            if (GetPlayerState(actor) != PropHuntPlayerState.Alive)
                continue;
            Player player = PhotonNetwork.CurrentRoom.GetPlayer(actor);
            double deadline = ReconnectDeadline(actor);
            PropHuntReconnectAction action = PropHuntRoundRules.EvaluateConnection(
                player != null, player != null && player.IsInactive, IsReady(actor), PhotonNetwork.Time, deadline);
            mustPause |= PropHuntRoundRules.MustPauseForRecovery(actor, hunter, action);
            if (action == PropHuntReconnectAction.Abandon)
            {
                if (actor == hunter)
                    return true;
                changes[StateKey(actor)] = (int)PropHuntPlayerState.Abandoned;
                changes[ReconnectKey(actor)] = 0d;
                continue;
            }
            if (action == PropHuntReconnectAction.StartGrace)
            {
                changes[ReconnectKey(actor)] = PhotonNetwork.Time + PropHuntRoundRules.ReconnectGraceSeconds;
                changes[TokenKey(actor)] = RecoveryToken(actor) + 1;
                CapturePose(actor, changes);
            }
            else if (action == PropHuntReconnectAction.Resume)
                changes[ReconnectKey(actor)] = 0d;
        }
        return false;
    }

    internal static void CapturePoses(Hashtable changes)
    {
        foreach (int actor in Participants())
            CapturePose(actor, changes);
    }

    private static void CapturePose(int actor, Hashtable changes)
    {
        TankController tank = TankController.FindForActor(actor, CurrentRound);
        if (tank != null && GetPlayerState(actor) == PropHuntPlayerState.Alive &&
            ReconnectDeadline(actor) == 0d)
        {
            Vector3 position = tank.transform.position;
            Quaternion rotation = tank.transform.rotation;
            if (!TryGetPose(actor, out Vector3 savedPosition, out Quaternion savedRotation) ||
                !position.Equals(savedPosition) || !rotation.Equals(savedRotation))
                changes[PoseKey(actor)] = new object[] { position, rotation };
        }
    }
}
