using System;

public static class PropHuntRoundRules
{
    public const int MaxPlayers = 4;

    public const int RequiredButtons = 5;
    public const int ButtonSpawnCount = 20;

    public const double HideDurationSeconds = 45d;
    public const double ButtonActivationRange = 2.2d;

    public const float MinPropScale = 0.5f;
    public const float MaxPropScale = 1.5f;



    public static bool CanStartGame(int playerCount)
    {
        return playerCount >= 1 &&
               playerCount <= MaxPlayers;
    }

    public static bool CanEnterRoom(
        bool isOpen,
        int playerCount,
        int maxPlayers)
    {
        return isOpen &&
               playerCount < MaxPlayers &&
               (maxPlayers == 0 ||
                playerCount < maxPlayers);
    }

    public static bool CanPropScale(
        GamePhase phase)
    {
        return phase == GamePhase.Hiding;
    }

    public static bool CanHunterMove(
        GamePhase phase)
    {
        return phase == GamePhase.Playing;
    }

    public static bool CanHunterShoot(
        GamePhase phase)
    {
        return phase == GamePhase.Playing;
    }

    public static bool IsHidingFinished(
        double now,
        double hideEndTime)
    {
        return IsFinite(now) &&
               IsFinite(hideEndTime) &&
               now >= hideEndTime;
    }

    public static bool CanEliminateProp(
        GamePhase phase,
        int targetActor,
        int hunterActor,
        bool targetAlive)
    {
        return phase == GamePhase.Playing &&
               targetActor > 0 &&
               targetActor != hunterActor &&
               targetAlive;
    }

    public static bool CanActivateButton(
        GamePhase phase,
        int senderActor,
        int hunterActor,
        bool senderAlive,
        int buttonIndex,
        bool alreadyActivated,
        double distanceSquared)
    {
        if (phase != GamePhase.Playing)
            return false;

        if (senderActor <= 0)
            return false;

        if (senderActor == hunterActor)
            return false;

        if (!senderAlive)
            return false;

        if (buttonIndex < 0 ||
            buttonIndex >= RequiredButtons)
        {
            return false;
        }

        if (alreadyActivated)
            return false;

        if (!IsFinite(distanceSquared) ||
            distanceSquared < 0d)
        {
            return false;
        }

        double maxDistanceSquared =
            ButtonActivationRange *
            ButtonActivationRange;

        return distanceSquared <=
               maxDistanceSquared;
    }

    public static bool AllButtonsActivated(
        bool[] activatedButtons)
    {
        if (activatedButtons == null ||
            activatedButtons.Length != RequiredButtons)
        {
            return false;
        }

        for (int i = 0;
             i < activatedButtons.Length;
             i++)
        {
            if (!activatedButtons[i])
                return false;
        }

        return true;
    }

    public static int CountActivatedButtons(
        bool[] activatedButtons)
    {
        if (activatedButtons == null)
            return 0;

        int count = 0;

        for (int i = 0;
             i < activatedButtons.Length;
             i++)
        {
            if (activatedButtons[i])
            {
                count++;
            }
        }

        return count;
    }

    public static int CountAliveProps(
        int hunterActor,
        int[] participants,
        bool[] aliveStates)
    {
        if (participants == null ||
            aliveStates == null ||
            participants.Length != aliveStates.Length)
        {
            throw new ArgumentException(
                "Participants and aliveStates must match."
            );
        }

        int aliveProps = 0;

        for (int i = 0;
             i < participants.Length;
             i++)
        {
            if (participants[i] ==
                hunterActor)
            {
                continue;
            }

            if (aliveStates[i])
            {
                aliveProps++;
            }
        }

        return aliveProps;
    }

    public static bool HunterWins(
        int aliveProps)
    {
        return aliveProps <= 0;
    }

    public static bool PropsWin(
        int activatedButtons)
    {
        return activatedButtons >=
               RequiredButtons;
    }

    public static float ClampPropScale(
        float scale)
    {
        if (scale < MinPropScale)
            return MinPropScale;

        if (scale > MaxPropScale)
            return MaxPropScale;

        return scale;
    }

    public static bool IsValidButtonSpawn(
        int spawnIndex)
    {
        return spawnIndex >= 0 &&
               spawnIndex < ButtonSpawnCount;
    }

    public static bool IsFinite(
        double value)
    {
        return !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }
}