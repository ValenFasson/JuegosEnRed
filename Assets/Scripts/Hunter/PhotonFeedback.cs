using System.Collections.Generic;
using Photon.Realtime;

public enum PhotonFeedbackSeverity
{
    Info,
    Warning,
    Error
}

public sealed class PhotonFeedbackNotice
{
    public string Message { get; }
    public PhotonFeedbackSeverity Severity { get; }
    public double ExpiresAt { get; }

    public PhotonFeedbackNotice(
        string message,
        PhotonFeedbackSeverity severity,
        double expiresAt)
    {
        Message = message;
        Severity = severity;
        ExpiresAt = expiresAt;
    }
}

public sealed class PhotonFeedbackHistory
{
    private const int Capacity = 5;

    private readonly List<PhotonFeedbackNotice> notices =
        new List<PhotonFeedbackNotice>();

    public IReadOnlyList<PhotonFeedbackNotice> Notices =>
        notices;

    public void Add(
        string message,
        PhotonFeedbackSeverity severity,
        double now)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Expire(now);

        if (notices.Count >= Capacity)
        {
            notices.RemoveAt(0);
        }

        double duration =
            severity == PhotonFeedbackSeverity.Error
                ? 15d
                : 8d;

        notices.Add(
            new PhotonFeedbackNotice(
                message,
                severity,
                now + duration
            )
        );
    }

    public void Expire(double now)
    {
        for (int i = notices.Count - 1;
             i >= 0;
             i--)
        {
            if (now >= notices[i].ExpiresAt)
            {
                notices.RemoveAt(i);
            }
        }
    }

    public void Clear()
    {
        notices.Clear();
    }
}

public sealed class PhotonPlayerFeedback
{
    private readonly Dictionary<int, string> names =
        new Dictionary<int, string>();

    private readonly HashSet<int> eliminatedPlayers =
        new HashSet<int>();

    public void Remember(
        int actorNumber,
        string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return;

        string cleanName =
            nickname
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\t', ' ')
                .Trim();

        if (cleanName.Length > 32)
        {
            cleanName =
                cleanName.Substring(0, 32);
        }

        names[actorNumber] = cleanName;
    }

    public string Name(int actorNumber)
    {
        if (names.TryGetValue(
            actorNumber,
            out string playerName))
        {
            return playerName;
        }

        return $"Jugador {actorNumber}";
    }

    public string Entered(
        int actorNumber,
        string nickname)
    {
        Remember(
            actorNumber,
            nickname
        );

        return $"{Name(actorNumber)} entró a la sala.";
    }

    public string Left(
        int actorNumber,
        string nickname)
    {
        Remember(
            actorNumber,
            nickname
        );

        return $"{Name(actorNumber)} abandonó la partida.";
    }

    public string Eliminated(
        int actorNumber,
        string nickname)
    {
        Remember(
            actorNumber,
            nickname
        );

        if (!eliminatedPlayers.Add(actorNumber))
            return null;

        return $"{Name(actorNumber)} fue eliminado.";
    }

    public void Clear()
    {
        names.Clear();
        eliminatedPlayers.Clear();
    }
}

public static class PhotonFeedbackText
{
    public static string RoomError(short code)
    {
        switch (code)
        {
            case ErrorCode.GameFull:
                return "La sala está llena.";

            case ErrorCode.GameClosed:
                return "La sala está cerrada.";

            case ErrorCode.GameDoesNotExist:
                return "La sala ya no existe.";

            case ErrorCode.GameIdAlreadyExists:
                return "Ya existe una sala con ese nombre.";

            case ErrorCode.InvalidAuthentication:
            case ErrorCode.CustomAuthenticationFailed:
                return "No se pudo validar la conexión con Photon.";

            case ErrorCode.AuthenticationTicketExpired:
                return "La sesión de Photon expiró.";

            case ErrorCode.MaxCcuReached:
                return "Photon alcanzó el límite de jugadores conectados.";

            case ErrorCode.InvalidRegion:
                return "La región configurada no está disponible.";

            case ErrorCode.OperationLimitReached:
                return "Se enviaron demasiadas solicitudes.";

            case ErrorCode.OperationNotAllowedInCurrentState:
                return "Photon todavía no permite realizar esta acción.";

            default:
                return "No se pudo completar la solicitud de sala.";
        }
    }

    public static string Disconnect(
        DisconnectCause cause)
    {
        switch (cause)
        {
            case DisconnectCause.InvalidAuthentication:
                return "No se pudo validar la configuración de Photon.";

            case DisconnectCause.CustomAuthenticationFailed:
                return "No se pudo validar la sesión.";

            case DisconnectCause.AuthenticationTicketExpired:
                return "La sesión de Photon expiró.";

            case DisconnectCause.MaxCcuReached:
                return "Photon alcanzó el límite de jugadores conectados.";

            case DisconnectCause.InvalidRegion:
                return "La región configurada no está disponible.";

            case DisconnectCause.ServerAddressInvalid:
                return "La dirección del servidor no es válida.";

            case DisconnectCause.ClientTimeout:
            case DisconnectCause.ServerTimeout:
                return "Se perdió la conexión con el servidor.";

            case DisconnectCause.ExceptionOnConnect:
            case DisconnectCause.DnsExceptionOnConnect:
                return "No se pudo conectar con el servidor.";

            case DisconnectCause.DisconnectByClientLogic:
            case DisconnectCause.ApplicationQuit:
                return "Te desconectaste de Photon.";

            default:
                return "Se interrumpió la conexión con Photon.";
        }
    }

    public static string Phase(
        GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Waiting:
                return "Esperando jugadores";

            case GamePhase.Hiding:
                return "Los props se están escondiendo";

            case GamePhase.Playing:
                return "Partida en curso";

            case GamePhase.Finished:
                return "Partida finalizada";

            default:
                return "Preparando partida";
        }
    }

    public static string HidingCountdown(
        int seconds)
    {
        return
            "Los props se están escondiendo\n" +
            $"Faltan {seconds} segundos para empezar";
    }

    public static string WaitingPlayers(
        int currentPlayers)
    {
        return
            $"Esperando jugadores: " +
            $"{currentPlayers}/" +
            $"{PropHuntRoundRules.MaxPlayers}";
    }

    public static string Result(
        GameWinner winner)
    {
        switch (winner)
        {
            case GameWinner.Hunter:
                return "Ganó el Hunter.";

            case GameWinner.Props:
                return "Ganaron los Props.";

            default:
                return "Partida finalizada.";
        }
    }
}