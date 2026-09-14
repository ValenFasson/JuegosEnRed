using System;
using System.Collections.Generic;
using Photon.Realtime;

public enum PhotonFeedbackSeverity { Info, Warning, Error }

public sealed class PhotonFeedbackNotice
{
    public string Message { get; }
    public PhotonFeedbackSeverity Severity { get; }
    public double ExpiresAt { get; }

    public PhotonFeedbackNotice(string message, PhotonFeedbackSeverity severity, double expiresAt)
    {
        Message = message;
        Severity = severity;
        ExpiresAt = expiresAt;
    }
}

// Local presentation only: never changes or replicates gameplay state.
public sealed class PhotonFeedbackHistory
{
    private const int Capacity = 5;
    private readonly List<PhotonFeedbackNotice> notices = new List<PhotonFeedbackNotice>();
    public IReadOnlyList<PhotonFeedbackNotice> Notices { get; }

    public PhotonFeedbackHistory() => Notices = notices.AsReadOnly();

    public void Add(string message, PhotonFeedbackSeverity severity, double now)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        Expire(now);
        if (notices.Count == Capacity)
            notices.RemoveAt(0);
        notices.Add(new PhotonFeedbackNotice(message, severity,
            now + (severity == PhotonFeedbackSeverity.Error ? 15d : 8d)));
    }

    public void Expire(double now)
    {
        for (int i = notices.Count - 1; i >= 0; i--)
            if (now >= notices[i].ExpiresAt)
                notices.RemoveAt(i);
    }
    public void Clear() => notices.Clear();
}

public sealed class PhotonPlayerFeedback
{
    private readonly Dictionary<int, string> names = new Dictionary<int, string>();
    private readonly Dictionary<int, PropHuntPlayerState> states = new Dictionary<int, PropHuntPlayerState>();
    private readonly HashSet<int> announcedAbandonments = new HashSet<int>();

    public void Remember(int actor, string nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            string name = nickname.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
            names[actor] = name.Length > 32 ? name.Substring(0, 32) : name;
        }
    }

    public string Name(int actor) => names.TryGetValue(actor, out string name) ? name : $"Jugador {actor}";

    public string Entered(int actor, string nickname, bool rejoined)
    {
        Remember(actor, nickname);
        if (rejoined && states.TryGetValue(actor, out PropHuntPlayerState state) && state == PropHuntPlayerState.Abandoned)
            return $"{Name(actor)} volvió a la sala, pero quedó fuera de esta ronda.";
        return rejoined ? $"{Name(actor)} restableció la conexión." : $"{Name(actor)} entró a la sala.";
    }

    public string Left(int actor, string nickname, bool inactive)
    {
        Remember(actor, nickname);
        if (!inactive)
            return Abandoned(actor);
        return announcedAbandonments.Contains(actor) ? null : $"{Name(actor)} perdió la conexión.";
    }

    public string ObserveState(int actor, PropHuntPlayerState state, bool announce)
    {
        bool unchanged = states.TryGetValue(actor, out PropHuntPlayerState previous) && previous == state;
        states[actor] = state;
        if (state == PropHuntPlayerState.Alive && !unchanged)
            announcedAbandonments.Remove(actor);
        if (!announce)
        {
            if (state == PropHuntPlayerState.Abandoned)
                announcedAbandonments.Add(actor);
            return null;
        }
        if (unchanged)
            return null;
        if (state == PropHuntPlayerState.Abandoned)
            return Abandoned(actor);
        return state == PropHuntPlayerState.Eliminated ? $"{Name(actor)} fue eliminado." : null;
    }

    private string Abandoned(int actor) => announcedAbandonments.Add(actor)
        ? $"{Name(actor)} abandonó la partida." : null;

    public void ResetRound() => states.Clear();
    public void Clear()
    {
        names.Clear();
        states.Clear();
        announcedAbandonments.Clear();
    }
}

public static class PhotonFeedbackText
{
    public static string RoomError(short code)
    {
        switch (code)
        {
            case ErrorCode.GameFull: return "La sala está llena. Elegí otra sala.";
            case ErrorCode.GameClosed: return "La sala está cerrada. Elegí otra sala.";
            case ErrorCode.GameDoesNotExist: return "La sala ya no existe. Volvé a seleccionar una sala.";
            case ErrorCode.GameIdAlreadyExists: return "La sala ya existe. Intentá entrar nuevamente.";
            case ErrorCode.JoinFailedWithRejoinerNotFound: return "Venció el plazo para recuperar tu lugar en la sala.";
            case ErrorCode.JoinFailedFoundActiveJoiner: return "Tu usuario ya está conectado a esa sala.";
            case ErrorCode.JoinFailedFoundInactiveJoiner: return "Tu lugar sigue reservado. Es necesario recuperar la sesión anterior.";
            case ErrorCode.InvalidAuthentication:
            case ErrorCode.CustomAuthenticationFailed: return "No se pudo validar la sesión.";
            case ErrorCode.AuthenticationTicketExpired: return "Tu sesión expiró. Volvé a conectarte.";
            case ErrorCode.MaxCcuReached: return "El servicio alcanzó su límite de jugadores conectados. Intentá más tarde.";
            case ErrorCode.InvalidRegion: return "El servicio no está disponible en la región configurada.";
            case ErrorCode.OperationLimitReached: return "Se enviaron demasiadas solicitudes. Esperá antes de intentar nuevamente.";
            case ErrorCode.OperationNotAllowedInCurrentState: return "La conexión todavía no permite esa acción. Esperá e intentá nuevamente.";
            default: return "No se pudo completar la solicitud de sala. Intentá nuevamente.";
        }
    }

    public static string Disconnect(DisconnectCause cause)
    {
        switch (cause)
        {
            case DisconnectCause.InvalidAuthentication: return "No se pudo validar la configuración de conexión.";
            case DisconnectCause.CustomAuthenticationFailed: return "No se pudo validar tu sesión.";
            case DisconnectCause.AuthenticationTicketExpired: return "Tu sesión expiró. Volvé a conectarte.";
            case DisconnectCause.MaxCcuReached: return "El servicio alcanzó su límite de jugadores conectados. Intentá más tarde.";
            case DisconnectCause.InvalidRegion: return "El servicio no está disponible en la región configurada.";
            case DisconnectCause.ServerAddressInvalid: return "La dirección del servidor no es válida.";
            case DisconnectCause.DisconnectByOperationLimit: return "Se enviaron demasiadas solicitudes. Esperá antes de reconectar.";
            case DisconnectCause.DisconnectByClientLogic:
            case DisconnectCause.ApplicationQuit: return "Te desconectaste de Photon.";
            case DisconnectCause.ClientTimeout:
            case DisconnectCause.ServerTimeout: return "Se perdió la conexión con el servidor. Revisá tu conexión e intentá nuevamente.";
            case DisconnectCause.ExceptionOnConnect:
            case DisconnectCause.DnsExceptionOnConnect: return "No se pudo contactar al servidor. Revisá tu conexión e intentá nuevamente.";
            default: return "Se interrumpió la conexión con Photon. Intentá nuevamente.";
        }
    }

    public static string Phase(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Waiting: return "Esperando jugadores";
            case GamePhase.Spawning: return "Preparando personajes";
            case GamePhase.Playing: return "Partida en curso";
            case GamePhase.Paused: return "Partida pausada: esperando al cazador";
            case GamePhase.Finished: return "Ronda finalizada";
            default: return "Preparando partida";
        }
    }

    public static string Result(string winner, string reason)
    {
        if (winner == "Hunter")
            return "Ganó el cazador.";
        if (winner == "Props")
            return "Ganaron los props.";
        if (reason == "HunterDisconnected")
            return "Ronda cancelada: el cazador abandonó la partida.";
        return "Ronda cancelada. Esperando la próxima ronda.";
    }
}
