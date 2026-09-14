using System;

// Pure rules: no Unity lifecycle or network side effects.
public static class PropHuntRoundRules
{
    public const int Protocol = 4;
    public const int RequiredButtons = 2;
    public const double ButtonActivationRange = 2.2d;
    public const int RequiredPlayers = 3;
    public const double ReconnectGraceSeconds = 60d;
    public const double RecoveryWindowSeconds = ReconnectGraceSeconds + 5d;
    public const double PreparationTimeoutSeconds = 20d;
    public const int PlayerTtlMilliseconds = 90000;
    public const int EmptyRoomTtlMilliseconds = 120000;

    // Inactive actors also occupy capacity: their reconnection slot stays reserved.
    public static bool CanEnterRoom(bool isOpen, int playerCount, int maxPlayers) => isOpen && (maxPlayers == 0 || playerCount < maxPlayers);

    public static bool CanShoot(GamePhase phase, int sender, int hunter, bool alive, bool connected, int requestedRound, int currentRound, int sequence, int lastSequence, double now, double nextFireAt)
    {
        return phase == GamePhase.Playing && sender > 0 &&
               sender == hunter && alive && connected &&
               requestedRound == currentRound && sequence > 0 &&
               lastSequence < int.MaxValue && sequence == lastSequence + 1 &&
               IsFinite(now) && IsFinite(nextFireAt) && now >= nextFireAt;
    }

    public static bool CanEliminate(GamePhase phase, int shotRound, int currentRound, int target, int hunter, PropHuntPlayerState state, bool activeShot)
    {
        return phase == GamePhase.Playing &&
               shotRound == currentRound && target > 0 && target != hunter &&
               state == PropHuntPlayerState.Alive && activeShot;
    }

    public static bool CanActivateButton(GamePhase phase, int sender, int hunter, bool participant, PropHuntPlayerState state, bool connected, int requestedRound, int currentRound, int buttonId, int activatedMask, double distanceSquared)
    {
        return phase == GamePhase.Playing && sender > 0 && sender != hunter &&
               participant && state == PropHuntPlayerState.Alive && connected &&
               requestedRound == currentRound && currentRound > 0 &&
               buttonId >= 0 && buttonId < RequiredButtons &&
               (activatedMask & (1 << buttonId)) == 0 &&
               IsFinite(distanceSquared) && distanceSquared >= 0d &&
               distanceSquared <= ButtonActivationRange * ButtonActivationRange;
    }

    public static bool AllButtonsActivated(int mask) => (mask & ((1 << RequiredButtons) - 1)) == (1 << RequiredButtons) - 1;

    public static int CountAliveProps(int hunter, int[] participants, PropHuntPlayerState[] states)
    {
        if (participants == null || states == null || participants.Length != states.Length)
            throw new ArgumentException("Participants and states must match.");

        int count = 0;
        for (int i = 0; i < participants.Length; i++)
        {
            if (participants[i] != hunter &&
                states[i] == PropHuntPlayerState.Alive)
                count++;
        }
        return count;
    }

    public static bool GraceExpired(double now, double deadline)
    {
        return IsFinite(now) && IsFinite(deadline) &&
               deadline > 0d && now >= deadline;
    }

    public static PropHuntReconnectAction EvaluateConnection(bool present, bool inactive, bool ready, double now, double deadline)
    {
        if (!present || GraceExpired(now, deadline))
            return PropHuntReconnectAction.Abandon;
        if (inactive)
            return deadline == 0d ? PropHuntReconnectAction.StartGrace : PropHuntReconnectAction.Wait;
        if (deadline > 0d)
            return ready ? PropHuntReconnectAction.Resume : PropHuntReconnectAction.Wait;
        return PropHuntReconnectAction.None;
    }

    public static bool MustPauseForRecovery(int actor, int hunter, PropHuntReconnectAction action) => actor == hunter && hunter > 0 && (action == PropHuntReconnectAction.StartGrace || action == PropHuntReconnectAction.Wait);

    public static bool HunterAllowsGameplay(bool present, bool inactive, bool ready, double deadline) => present && !inactive && ready && deadline == 0d;

    public static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static bool PreserveRecoveryRoom(bool hasRoom, bool leavingForMaster, bool quitting)
    {
        return hasRoom && !leavingForMaster && !quitting;
    }
}
