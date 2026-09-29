using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public enum QuizMatchState : byte
    {
        WaitingForPlayers = 0,
        Ready = 1,
        BuzzerOpen = 2,
        AwaitingAnswer = 3,
        Finished = 4
    }

    /// <summary>
    /// Partida de una pregunta para dos jugadores. El Master Client valida el
    /// pulsador, la respuesta y el ganador mediante RPCs de un PhotonView.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public sealed class QuizGame : MonoBehaviourPunCallbacks
    {
        private const string MatchProperty = "q.id";
        private const string StateProperty = "q.state";
        private const string StartTimeProperty = "q.time";
        private const string StartTimestampProperty = "q.start";
        private const string AnsweringActorProperty = "q.actor";
        private const string BuzzTimestampProperty = "q.buzz";
        private const string SelectedAnswerProperty = "q.answer";
        private const string CorrectProperty = "q.correct";
        private const string WinnerProperty = "q.winner";
        private const string FinishedTimestampProperty = "q.end";

        [Header("Pregunta única")]
        [TextArea(2, 4)]
        [SerializeField] private string question = "¿Cuál es el planeta más grande del Sistema Solar?";

        [SerializeField] private string[] answers =
        {
            "Saturno",
            "Júpiter",
            "Neptuno",
            "Urano"
        };

        [SerializeField, Range(0, 3)] private int correctAnswerIndex = 1;

        [Header("Tiempos")]
        [Tooltip("Ventana para recibir pulsaciones cercanas antes de decidir el primero.")]
        [SerializeField, Min(0)] private int arbitrationWindowMilliseconds = 120;

        [Tooltip("Tiempo máximo desde el inicio para aceptar una pulsación.")]
        [SerializeField, Min(1f)] private float maximumBuzzSeconds = 20f;

        private readonly Dictionary<int, int> buzzCandidates = new Dictionary<int, int>(2);

        private QuizMatchState state = QuizMatchState.WaitingForPlayers;
        private int matchId;
        private int startTimestamp;
        private int answeringActorNumber = -1;
        private int buzzTimestamp;
        private int selectedAnswerIndex = -1;
        private int winnerActorNumber = -1;
        private int finishedTimestamp;
        private int firstBuzzReceivedTimestamp;
        private double startNetworkTime;
        private bool answerWasCorrect;
        private bool localBuzzSent;
        private bool localAnswerSent;
        private bool arbitrationRunning;

        public event Action StateChanged;

        public string Question => question;
        public IReadOnlyList<string> Answers => answers;
        public QuizMatchState State => state;
        public int MatchId => matchId;
        public int PlayerCount => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
        public int AnsweringActorNumber => answeringActorNumber;
        public int SelectedAnswerIndex => selectedAnswerIndex;
        public int CorrectAnswerIndex => correctAnswerIndex;
        public bool AnswerWasCorrect => answerWasCorrect;
        public int WinnerActorNumber => winnerActorNumber;
        public bool LocalBuzzSent => localBuzzSent;
        public string AnsweringPlayerName => GetPlayerName(answeringActorNumber);
        public string WinnerPlayerName => GetPlayerName(winnerActorNumber);

        public bool CanStartMatch => PhotonNetwork.InRoom &&
            PhotonNetwork.IsMasterClient &&
            PhotonNetwork.CurrentRoom.PlayerCount == 2 &&
            (state == QuizMatchState.Ready || state == QuizMatchState.Finished);

        public bool CanPressBuzzer => PhotonNetwork.InRoom &&
            state == QuizMatchState.BuzzerOpen &&
            !localBuzzSent;

        public bool CanLocalPlayerAnswer => PhotonNetwork.InRoom &&
            state == QuizMatchState.AwaitingAnswer &&
            PhotonNetwork.LocalPlayer.ActorNumber == answeringActorNumber &&
            !localAnswerSent;

        public bool IsLocalWinner => PhotonNetwork.InRoom &&
            state == QuizMatchState.Finished &&
            PhotonNetwork.LocalPlayer.ActorNumber == winnerActorNumber;

        /// <summary>
        /// Segundos transcurridos desde el comienzo, calculados con
        /// PhotonNetwork.Time mientras la partida está activa.
        /// </summary>
        public double ElapsedGameSeconds
        {
            get
            {
                if (!PhotonNetwork.InRoom ||
                    state == QuizMatchState.WaitingForPlayers ||
                    state == QuizMatchState.Ready)
                {
                    return 0d;
                }

                if (state == QuizMatchState.Finished)
                {
                    return Math.Max(
                        0d,
                        PhotonTimestamp.DeltaMilliseconds(finishedTimestamp, startTimestamp) / 1000d);
                }

                return PhotonTimestamp.ElapsedNetworkSeconds(
                    PhotonNetwork.Time,
                    startNetworkTime);
            }
        }

        public int WinningBuzzMilliseconds => answeringActorNumber < 0
            ? -1
            : PhotonTimestamp.DeltaMilliseconds(buzzTimestamp, startTimestamp);

        public string StatusText
        {
            get
            {
                switch (state)
                {
                    case QuizMatchState.WaitingForPlayers:
                        return $"Esperando jugadores ({PlayerCount}/2)";
                    case QuizMatchState.Ready:
                        return PhotonNetwork.IsMasterClient
                            ? "Dos jugadores listos. Inicia la pregunta."
                            : "Esperando que el Master inicie la pregunta.";
                    case QuizMatchState.BuzzerOpen:
                        return localBuzzSent ? "Pulsación enviada" : "¡Pulsa para responder!";
                    case QuizMatchState.AwaitingAnswer:
                        return CanLocalPlayerAnswer
                            ? "Fuiste primero. Elige una respuesta."
                            : $"{AnsweringPlayerName} respondió primero.";
                    case QuizMatchState.Finished:
                        return BuildFinishedStatus();
                    default:
                        return string.Empty;
                }
            }
        }

        private void Start()
        {
            if (PhotonNetwork.InRoom)
            {
                ApplyRoomState();
                InitializeRoomStateIfNeeded();
            }
        }

        private void Update()
        {
            if (CanPressBuzzer && Keyboard.current?.spaceKey.wasPressedThisFrame == true)
            {
                PressBuzzer();
            }

            if (!PhotonNetwork.IsMasterClient ||
                state != QuizMatchState.BuzzerOpen)
            {
                return;
            }

            int now = PhotonNetwork.ServerTimestamp;

            if (arbitrationRunning &&
                PhotonTimestamp.DeltaMilliseconds(now, firstBuzzReceivedTimestamp) >=
                arbitrationWindowMilliseconds)
            {
                ResolveBuzzWinner();
                return;
            }

            if (PhotonTimestamp.DeltaMilliseconds(now, startTimestamp) >=
                Mathf.CeilToInt(maximumBuzzSeconds * 1000f))
            {
                FinishWithoutWinner();
            }
        }

        /// <summary>Conectar al botón Iniciar. Sólo funciona para el Master.</summary>
        public void StartMatch()
        {
            if (!CanStartMatch)
            {
                Debug.LogWarning("La partida requiere dos jugadores y sólo puede iniciarla el Master.", this);
                return;
            }

            if (!HasValidQuestionConfiguration())
            {
                Debug.LogError("La pregunta necesita cuatro respuestas y un índice correcto válido.", this);
                return;
            }

            int nextMatchId = ReadRoomInt(MatchProperty, 0) + 1;
            Hashtable properties = new Hashtable
            {
                [MatchProperty] = nextMatchId,
                [StateProperty] = (byte)QuizMatchState.BuzzerOpen,
                [StartTimeProperty] = PhotonNetwork.Time,
                [StartTimestampProperty] = PhotonNetwork.ServerTimestamp,
                [AnsweringActorProperty] = -1,
                [BuzzTimestampProperty] = 0,
                [SelectedAnswerProperty] = -1,
                [CorrectProperty] = false,
                [WinnerProperty] = -1,
                [FinishedTimestampProperty] = 0
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }

        /// <summary>Conectar al botón pulsador de ambos jugadores.</summary>
        public void PressBuzzer()
        {
            if (!CanPressBuzzer)
            {
                return;
            }

            localBuzzSent = true;
            int pressedAt = PhotonNetwork.ServerTimestamp;

            if (!HasUsablePhotonView())
            {
                localBuzzSent = false;
                StateChanged?.Invoke();
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                RegisterBuzzCandidate(PhotonNetwork.LocalPlayer.ActorNumber, matchId, pressedAt);
                StateChanged?.Invoke();
                return;
            }

            photonView.RPC(
                nameof(RequestBuzzRpc),
                RpcTarget.MasterClient,
                matchId,
                pressedAt);
            PhotonNetwork.SendAllOutgoingCommands();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Conectar los cuatro botones de respuesta pasando 0, 1, 2 y 3.
        /// </summary>
        public void SubmitAnswer(int answerIndex)
        {
            if (!CanLocalPlayerAnswer || answerIndex < 0 || answerIndex >= answers.Length)
            {
                return;
            }

            localAnswerSent = true;

            if (!HasUsablePhotonView())
            {
                localAnswerSent = false;
                StateChanged?.Invoke();
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                ProcessAnswer(PhotonNetwork.LocalPlayer.ActorNumber, matchId, answerIndex);
                StateChanged?.Invoke();
                return;
            }

            photonView.RPC(
                nameof(RequestAnswerRpc),
                RpcTarget.MasterClient,
                matchId,
                answerIndex);
            PhotonNetwork.SendAllOutgoingCommands();
            StateChanged?.Invoke();
        }

        [PunRPC]
        private void RequestBuzzRpc(
            int requestMatchId,
            int pressedAt,
            PhotonMessageInfo info)
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            RegisterBuzzCandidate(info.Sender.ActorNumber, requestMatchId, pressedAt);
        }

        [PunRPC]
        private void RequestAnswerRpc(
            int requestMatchId,
            int answerIndex,
            PhotonMessageInfo info)
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            ProcessAnswer(info.Sender.ActorNumber, requestMatchId, answerIndex);
        }

        public override void OnJoinedRoom()
        {
            ApplyRoomState();
            InitializeRoomStateIfNeeded();
        }

        public override void OnLeftRoom()
        {
            ResetLocalState();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (PhotonNetwork.IsMasterClient &&
                PhotonNetwork.CurrentRoom.PlayerCount == 2 &&
                state == QuizMatchState.WaitingForPlayers)
            {
                SetReadyState();
            }

            StateChanged?.Invoke();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (state == QuizMatchState.BuzzerOpen || state == QuizMatchState.AwaitingAnswer)
                {
                    FinishMatch(PhotonNetwork.LocalPlayer.ActorNumber, -1, false);
                }
                else
                {
                    SetWaitingState();
                }
            }

            StateChanged?.Invoke();
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            buzzCandidates.Clear();
            arbitrationRunning = false;

            if (!newMasterClient.IsLocal)
            {
                return;
            }

            // Las solicitudes pendientes fueron dirigidas al Master anterior.
            // Reiniciamos a un estado conocido en vez de inventar un resultado.
            if (PhotonNetwork.CurrentRoom.PlayerCount == 2)
            {
                SetReadyState();
            }
            else
            {
                SetWaitingState();
            }
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (propertiesThatChanged.ContainsKey(StateProperty) ||
                propertiesThatChanged.ContainsKey(MatchProperty) ||
                propertiesThatChanged.ContainsKey(AnsweringActorProperty) ||
                propertiesThatChanged.ContainsKey(WinnerProperty))
            {
                ApplyRoomState();
            }
        }

        private void RegisterBuzzCandidate(int actorNumber, int requestMatchId, int pressedAt)
        {
            if (state != QuizMatchState.BuzzerOpen ||
                requestMatchId != matchId ||
                buzzCandidates.ContainsKey(actorNumber) ||
                PhotonNetwork.CurrentRoom.GetPlayer(actorNumber) == null)
            {
                return;
            }

            int responseMilliseconds = PhotonTimestamp.DeltaMilliseconds(
                pressedAt,
                startTimestamp);

            if (responseMilliseconds < 0 ||
                responseMilliseconds > Mathf.CeilToInt(maximumBuzzSeconds * 1000f))
            {
                return;
            }

            buzzCandidates.Add(actorNumber, pressedAt);

            if (!arbitrationRunning)
            {
                arbitrationRunning = true;
                firstBuzzReceivedTimestamp = PhotonNetwork.ServerTimestamp;
            }
        }

        private void ProcessAnswer(int actorNumber, int requestMatchId, int answerIndex)
        {
            if (state != QuizMatchState.AwaitingAnswer ||
                requestMatchId != matchId ||
                actorNumber != answeringActorNumber ||
                answerIndex < 0 ||
                answerIndex >= answers.Length)
            {
                return;
            }

            bool isCorrect = answerIndex == correctAnswerIndex;
            int winner = isCorrect ? answeringActorNumber : FindOpponentActor(answeringActorNumber);
            FinishMatch(winner, answerIndex, isCorrect);
        }

        private void ResolveBuzzWinner()
        {
            arbitrationRunning = false;

            int selectedActor = -1;
            int selectedTimestamp = 0;

            foreach (KeyValuePair<int, int> candidate in buzzCandidates)
            {
                bool isFirst = selectedActor < 0 ||
                    PhotonTimestamp.IsEarlier(candidate.Value, selectedTimestamp) ||
                    (candidate.Value == selectedTimestamp && candidate.Key < selectedActor);

                if (isFirst)
                {
                    selectedActor = candidate.Key;
                    selectedTimestamp = candidate.Value;
                }
            }

            if (selectedActor < 0)
            {
                FinishWithoutWinner();
                return;
            }

            Hashtable properties = new Hashtable
            {
                [StateProperty] = (byte)QuizMatchState.AwaitingAnswer,
                [AnsweringActorProperty] = selectedActor,
                [BuzzTimestampProperty] = selectedTimestamp
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }

        private void FinishMatch(int winner, int answerIndex, bool isCorrect)
        {
            Hashtable properties = new Hashtable
            {
                [StateProperty] = (byte)QuizMatchState.Finished,
                [SelectedAnswerProperty] = answerIndex,
                [CorrectProperty] = isCorrect,
                [WinnerProperty] = winner,
                [FinishedTimestampProperty] = PhotonNetwork.ServerTimestamp
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }

        private void FinishWithoutWinner()
        {
            FinishMatch(-1, -1, false);
        }

        private void InitializeRoomStateIfNeeded()
        {
            if (!PhotonNetwork.IsMasterClient ||
                PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(StateProperty))
            {
                return;
            }

            if (PhotonNetwork.CurrentRoom.PlayerCount == 2)
            {
                SetReadyState();
            }
            else
            {
                SetWaitingState();
            }
        }

        private void SetReadyState()
        {
            SetLobbyState(QuizMatchState.Ready);
        }

        private void SetWaitingState()
        {
            SetLobbyState(QuizMatchState.WaitingForPlayers);
        }

        private void SetLobbyState(QuizMatchState lobbyState)
        {
            Hashtable properties = new Hashtable
            {
                [StateProperty] = (byte)lobbyState,
                [AnsweringActorProperty] = -1,
                [BuzzTimestampProperty] = 0,
                [SelectedAnswerProperty] = -1,
                [CorrectProperty] = false,
                [WinnerProperty] = -1,
                [FinishedTimestampProperty] = 0
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }

        private void ApplyRoomState()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            {
                return;
            }

            Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;
            int previousMatchId = matchId;

            matchId = ReadInt(properties, MatchProperty, 0);
            state = (QuizMatchState)ReadByte(
                properties,
                StateProperty,
                (byte)QuizMatchState.WaitingForPlayers);
            startNetworkTime = ReadDouble(properties, StartTimeProperty, PhotonNetwork.Time);
            startTimestamp = ReadInt(properties, StartTimestampProperty, PhotonNetwork.ServerTimestamp);
            answeringActorNumber = ReadInt(properties, AnsweringActorProperty, -1);
            buzzTimestamp = ReadInt(properties, BuzzTimestampProperty, 0);
            selectedAnswerIndex = ReadInt(properties, SelectedAnswerProperty, -1);
            answerWasCorrect = ReadBool(properties, CorrectProperty, false);
            winnerActorNumber = ReadInt(properties, WinnerProperty, -1);
            finishedTimestamp = ReadInt(properties, FinishedTimestampProperty, 0);

            if (matchId != previousMatchId)
            {
                localBuzzSent = false;
                localAnswerSent = false;
                arbitrationRunning = false;
                buzzCandidates.Clear();
            }

            StateChanged?.Invoke();
        }

        private void ResetLocalState()
        {
            state = QuizMatchState.WaitingForPlayers;
            matchId = 0;
            startTimestamp = 0;
            answeringActorNumber = -1;
            buzzTimestamp = 0;
            selectedAnswerIndex = -1;
            winnerActorNumber = -1;
            finishedTimestamp = 0;
            startNetworkTime = 0d;
            answerWasCorrect = false;
            localBuzzSent = false;
            localAnswerSent = false;
            arbitrationRunning = false;
            buzzCandidates.Clear();
            StateChanged?.Invoke();
        }

        private bool HasValidQuestionConfiguration()
        {
            return !string.IsNullOrWhiteSpace(question) &&
                answers != null &&
                answers.Length == 4 &&
                correctAnswerIndex >= 0 &&
                correctAnswerIndex < answers.Length;
        }

        private bool HasUsablePhotonView()
        {
            if (photonView != null && photonView.ViewID != 0)
            {
                return true;
            }

            Debug.LogError(
                "QuizGame necesita un PhotonView de escena con un ViewID válido.",
                this);
            return false;
        }

        private int FindOpponentActor(int actorNumber)
        {
            foreach (Player player in PhotonNetwork.PlayerList)
            {
                if (player.ActorNumber != actorNumber)
                {
                    return player.ActorNumber;
                }
            }

            return -1;
        }

        private string BuildFinishedStatus()
        {
            if (winnerActorNumber < 0)
            {
                return "Terminó el tiempo sin respuestas.";
            }

            if (selectedAnswerIndex < 0)
            {
                return $"{WinnerPlayerName} gana por desconexión.";
            }

            return answerWasCorrect
                ? $"Respuesta correcta. Gana {WinnerPlayerName}."
                : $"Respuesta incorrecta. Gana {WinnerPlayerName}.";
        }

        private static string GetPlayerName(int actorNumber)
        {
            if (!PhotonNetwork.InRoom || actorNumber < 0)
            {
                return string.Empty;
            }

            Player player = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);
            return string.IsNullOrWhiteSpace(player?.NickName)
                ? $"Jugador {actorNumber}"
                : player.NickName;
        }

        private static int ReadRoomInt(string key, int fallback)
        {
            return PhotonNetwork.CurrentRoom != null
                ? ReadInt(PhotonNetwork.CurrentRoom.CustomProperties, key, fallback)
                : fallback;
        }

        private static int ReadInt(Hashtable properties, string key, int fallback)
        {
            return properties.TryGetValue(key, out object value) && value is int typed
                ? typed
                : fallback;
        }

        private static byte ReadByte(Hashtable properties, string key, byte fallback)
        {
            return properties.TryGetValue(key, out object value) && value is byte typed
                ? typed
                : fallback;
        }

        private static double ReadDouble(Hashtable properties, string key, double fallback)
        {
            return properties.TryGetValue(key, out object value) && value is double typed
                ? typed
                : fallback;
        }

        private static bool ReadBool(Hashtable properties, string key, bool fallback)
        {
            return properties.TryGetValue(key, out object value) && value is bool typed
                ? typed
                : fallback;
        }
    }
