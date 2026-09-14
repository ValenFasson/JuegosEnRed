using System;
using System.Text;
using Photon.Pun;
using UnityEngine;

public sealed class PhotonLocalValidation : MonoBehaviour
{
    [SerializeField] private bool logState;
    [SerializeField] private float logInterval = 1f;

    private float nextLogTime;
    private string lastState;

    private void Update()
    {
        if (!logState)
            return;

        if (Time.unscaledTime < nextLogTime)
            return;

        nextLogTime =
            Time.unscaledTime + logInterval;

        string state =
            DescribeState();

        if (state == lastState)
            return;

        lastState = state;

        Debug.Log(
            "[Photon State]\n" + state
        );
    }

    public static string DescribeState()
    {
        StringBuilder text =
            new StringBuilder();

        text.AppendLine(
            $"Connection: {PhotonNetwork.NetworkClientState}"
        );

        text.AppendLine(
            $"Region: {PhotonNetwork.CloudRegion}"
        );

        text.AppendLine(
            $"Ping: {PhotonNetwork.GetPing()} ms"
        );

        if (!PhotonNetwork.InRoom)
        {
            text.Append(
                PhotonNetwork.InLobby
                    ? "Estado: Lobby"
                    : "Estado: fuera de una Room"
            );

            return text.ToString();
        }

        text.AppendLine(
            $"Room: {PhotonNetwork.CurrentRoom.Name}"
        );

        text.AppendLine(
            $"Players: {PhotonNetwork.CurrentRoom.PlayerCount}/" +
            $"{PhotonNetwork.CurrentRoom.MaxPlayers}"
        );

        text.AppendLine(
            $"Local Actor: #{PhotonNetwork.LocalPlayer.ActorNumber}"
        );

        text.AppendLine(
            $"Master Actor: #{PhotonNetwork.CurrentRoom.MasterClientId}"
        );

        GamePhase phase =
            ReadPhase();

        int hunter =
            ReadInt(
                "hunter",
                -1
            );

        GameWinner winner =
            (GameWinner)ReadInt(
                "winner",
                (int)GameWinner.None
            );

        text.AppendLine(
            $"Phase: {phase}"
        );

        text.AppendLine(
            hunter > 0
                ? $"Hunter: #{hunter}"
                : "Hunter: pendiente"
        );

        if (phase == GamePhase.Hiding)
        {
            double hideEnd =
                ReadDouble(
                    "hideEnd",
                    0d
                );

            double remaining =
                Math.Max(
                    0d,
                    hideEnd -
                    PhotonNetwork.Time
                );

            text.AppendLine(
                $"Hiding: {remaining:F1}s"
            );
        }

        int activatedButtons = 0;

        for (int i = 0;
             i < PropHuntRoundRules.RequiredButtons;
             i++)
        {
            if (ReadBool(
                $"buttonUsed{i}",
                false))
            {
                activatedButtons++;
            }
        }

        text.AppendLine(
            $"Buttons: {activatedButtons}/" +
            $"{PropHuntRoundRules.RequiredButtons}"
        );

        if (phase == GamePhase.Finished)
        {
            text.AppendLine(
                $"Winner: {winner}"
            );
        }

        return text.ToString();
    }

    private static GamePhase ReadPhase()
    {
        return (GamePhase)ReadInt(
            "phase",
            (int)GamePhase.Waiting
        );
    }

    private static int ReadInt(
        string key,
        int defaultValue)
    {
        if (!PhotonNetwork.InRoom)
            return defaultValue;

        object value =
            PhotonNetwork.CurrentRoom
                .CustomProperties[key];

        return value is int result
            ? result
            : defaultValue;
    }

    private static bool ReadBool(
        string key,
        bool defaultValue)
    {
        if (!PhotonNetwork.InRoom)
            return defaultValue;

        object value =
            PhotonNetwork.CurrentRoom
                .CustomProperties[key];

        return value is bool result
            ? result
            : defaultValue;
    }

    private static double ReadDouble(
        string key,
        double defaultValue)
    {
        if (!PhotonNetwork.InRoom)
            return defaultValue;

        object value =
            PhotonNetwork.CurrentRoom
                .CustomProperties[key];

        if (value is double result)
            return result;

        return defaultValue;
    }
}