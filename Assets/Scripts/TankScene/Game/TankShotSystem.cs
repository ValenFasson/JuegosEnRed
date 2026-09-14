using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using static PropHuntGame;

// Accepted shots remain durable so a replacement Master can finish spawning them.
internal sealed class TankShotSystem
{
    internal const string LastShotKey = "lastShot";
    internal const string NextFireKey = "nextFireAt";
    private const string ShotPrefix = "shot_";
    private readonly PropHuntGame game;
    private readonly Dictionary<int, GameObject> requestedProjectiles =
        new Dictionary<int, GameObject>();

    internal TankShotSystem(PropHuntGame game) => this.game = game;
    internal void Reset() => requestedProjectiles.Clear();

    internal void OnPropertiesChanged(Hashtable changed)
    {
        foreach (System.Collections.DictionaryEntry entry in changed)
            if (entry.Value == null && TryReadSequence(entry.Key, out int sequence))
                requestedProjectiles.Remove(sequence);
    }

    private static bool TryReadSequence(object key, out int sequence)
    {
        sequence = 0;
        return key is string text && text.StartsWith(ShotPrefix, StringComparison.Ordinal) &&
            int.TryParse(text.Substring(ShotPrefix.Length), out sequence);
    }

    private static bool IsValidDirection(Vector3 direction) =>
        PropHuntRoundRules.IsFinite(direction.x) && PropHuntRoundRules.IsFinite(direction.y) &&
        PropHuntRoundRules.IsFinite(direction.z) &&
        direction.sqrMagnitude >= 0.5f && direction.sqrMagnitude <= 1.5f;

    internal static Hashtable ConsumeShot(int sequence) =>
        new Hashtable { { ShotPrefix + sequence, null } };

    internal bool TryAcceptShot(TankController source, Player sender, int round, int sequence, Vector3 direction)
    {
        if (!game.CanProcessPlayerAction(source, sender, round) || !IsValidDirection(direction))
            return false;
        if (!IsHunterAvailableForGameplay())
            return false;
        if (!PropHuntRoundRules.CanShoot(Phase, sender.ActorNumber, ReadInt(ShooterActorKey),
                GetPlayerState(sender.ActorNumber) == PropHuntPlayerState.Alive,
                !sender.IsInactive && IsReady(sender.ActorNumber), round, CurrentRound,
                sequence, LastAcceptedShot, PhotonNetwork.Time, ReadDouble(NextFireKey)) ||
            !source.TryGetProjectileConfiguration(out TankProjectile prefab))
            return false;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.5f || Vector3.Angle(source.transform.forward, direction) > 45f)
            return false;
        Quaternion rotation = Quaternion.LookRotation(direction.normalized);
        Vector3 origin = source.transform.position + direction.normalized * source.ProjectileSpawnDistance + Vector3.up * 0.5f;
        // Accepted shots are journaled before spawning, so a new Master can repair an interrupted spawn.
        return game.Commit(new Hashtable
        {
            { LastShotKey, sequence }, { NextFireKey, PhotonNetwork.Time + source.FireCooldown },
            { ShotPrefix + sequence, new object[]
                { CurrentRound, sender.ActorNumber, origin, rotation, prefab.Speed, PhotonNetwork.Time + prefab.Lifetime } }
        });
    }

    internal static bool TryGetShot(int sequence, out object[] data)
        => TryReadShot(Properties, CurrentRound, ReadInt(ShooterActorKey), sequence, out data);

    internal static bool TryReadShot(Hashtable properties, int currentRound, int hunter, int sequence,
        out object[] data)
    {
        data = properties != null ? properties[ShotPrefix + sequence] as object[] : null;
        return data != null && data.Length == 6 && data[0] is int round && round == currentRound &&
               data[1] is int actor && actor == hunter &&
               data[2] is Vector3 && data[3] is Quaternion && data[4] is float && data[5] is double;
    }

    internal static bool IsShotActive(int round, int sequence) =>
        PhotonNetwork.InRoom && round == CurrentRound && Phase == GamePhase.Playing &&
        TryGetShot(sequence, out object[] data) && PhotonNetwork.Time < (double)data[5];

    internal void ReconcileProjectiles()
    {
        Hashtable expired = new Hashtable();
        foreach (System.Collections.DictionaryEntry entry in Properties)
        {
            if (!TryReadSequence(entry.Key, out int sequence))
                continue;
            if (!TryGetShot(sequence, out object[] data) || PhotonNetwork.Time >= (double)data[5])
            {
                expired[entry.Key] = null;
                requestedProjectiles.Remove(sequence);
                continue;
            }
            TankProjectile existing = TankProjectile.FindShot(CurrentRound, sequence);
            if (existing != null)
            {
                TankProjectile.RemoveDuplicateShots(CurrentRound, sequence, existing);
                continue;
            }
            if (requestedProjectiles.TryGetValue(sequence, out GameObject requested) && requested != null)
                continue;
            TankController hunter = TankController.FindForActor(ReadInt(ShooterActorKey), CurrentRound);
            if (hunter != null && hunter.TryGetProjectileConfiguration(out TankProjectile prefab))
            {
                requestedProjectiles[sequence] = PhotonNetwork.InstantiateRoomObject(prefab.gameObject.name,
                    (Vector3)data[2], (Quaternion)data[3], 0,
                    new object[] { PropHuntRoundRules.Protocol, CurrentRound, sequence, (int)data[1], (float)data[4], (double)data[5] });
            }
        }
        if (expired.Count > 0)
            game.Commit(expired);
    }

    internal static void ClearShotProperties(Hashtable changes)
    {
        foreach (System.Collections.DictionaryEntry entry in Properties)
        {
            if (entry.Key is string key && key.StartsWith(ShotPrefix, StringComparison.Ordinal))
                changes[key] = null;
        }
    }
}
