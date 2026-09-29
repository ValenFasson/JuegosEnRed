using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
    /// Conecta a Photon y crea o entra a una sala privada para dos jugadores.
    /// No requiere PhotonView.
    /// </summary>
    public sealed class QuizConnection : MonoBehaviourPunCallbacks
    {
        [SerializeField] private string roomName = "Quiz-1v1";
        [SerializeField] private string playerName = string.Empty;
        [SerializeField] private string gameVersion = "1";
        [SerializeField] private bool connectOnStart;

        private bool isBusy;
        private string status = "Sin conectar";

        public event Action StateChanged;

        public bool IsBusy => isBusy;
        public bool IsInRoom => PhotonNetwork.InRoom;
        public string Status => status;
        public int PlayerCount => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 0;

        private void Start()
        {
            if (connectOnStart)
            {
                ConnectAndJoin();
            }
        }

        /// <summary>Conectar este método al botón de conexión.</summary>
        public void ConnectAndJoin()
        {
            if (PhotonNetwork.InRoom)
            {
                UpdateRoomStatus();
                return;
            }

            if (PhotonNetwork.IsConnectedAndReady)
            {
                JoinRoom();
                return;
            }

            if (isBusy)
            {
                return;
            }

            PhotonNetwork.NickName = string.IsNullOrWhiteSpace(playerName)
                ? $"Jugador-{Guid.NewGuid().ToString("N").Substring(0, 4)}"
                : playerName.Trim();
            PhotonNetwork.GameVersion = gameVersion;

            SetStatus("Conectando con Photon...", true);
            if (!PhotonNetwork.ConnectUsingSettings())
            {
                SetStatus("No se pudo iniciar la conexión", false);
            }
        }

        public override void OnConnectedToMaster()
        {
            JoinRoom();
        }

        public override void OnJoinedRoom()
        {
            UpdateRoomStatus();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            UpdateRoomStatus();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            UpdateRoomStatus();
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            SetStatus($"No se pudo entrar a la sala ({returnCode}): {message}", false);
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            SetStatus($"No se pudo crear la sala ({returnCode}): {message}", false);
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            SetStatus($"Desconectado: {cause}", false);
        }

        private void JoinRoom()
        {
            RoomOptions options = new RoomOptions
            {
                MaxPlayers = 2,
                IsOpen = true,
                IsVisible = false,
                CleanupCacheOnLeave = true
            };

            SetStatus($"Entrando a la sala {roomName}...", true);
            if (!PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default))
            {
                SetStatus("Photon no pudo encolar la entrada a la sala", false);
            }
        }

        private void UpdateRoomStatus()
        {
            if (!PhotonNetwork.InRoom)
            {
                SetStatus("Sin sala", false);
                return;
            }

            SetStatus(
                $"Sala {PhotonNetwork.CurrentRoom.Name}: {PhotonNetwork.CurrentRoom.PlayerCount}/2",
                false);
        }

        private void SetStatus(string value, bool busy)
        {
            status = value;
            isBusy = busy;
            StateChanged?.Invoke();
        }
    }
