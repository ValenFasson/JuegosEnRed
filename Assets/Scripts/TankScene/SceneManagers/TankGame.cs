using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public sealed class TankGame : MonoBehaviourPunCallbacks
{
    public const string AliveKey = "alive";

    private const string ArenaSeedKey = "arenaSeed";
    private const string GameStartedKey = "gameStarted";
    private const string WinnerKey = "winnerActor";

    private const byte RequiredPlayers = 3;

    [Header("Photon")]
    [SerializeField] private string gameVersion = "0.1";
    [SerializeField] private string roomName = "TankRoom";

    [Header("Player")]
    [SerializeField] private GameObject tankPrefab;
    [SerializeField] private Transform spawnPoint;

    [Header("Procedural Arena")]
    [SerializeField] private float arenaSize = 20f;
    [SerializeField] private int obstacleCount = 12;

    private bool arenaGenerated;
    private bool localTankSpawned;

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

    private void Start()
    {
        PhotonNetwork.GameVersion = gameVersion;

        // Este proyecto usa una sola escena.
        PhotonNetwork.AutomaticallySyncScene = false;

        if (PhotonNetwork.InRoom)
        {
            HandleJoinedRoom();
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            if (PhotonNetwork.InLobby)
                JoinOrCreateTankRoom();
            else
                PhotonNetwork.JoinLobby();

            return;
        }

        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        JoinOrCreateTankRoom();
    }

    private void JoinOrCreateTankRoom()
    {
        Hashtable roomProperties = new Hashtable
        {
            { ArenaSeedKey, UnityEngine.Random.Range(1, int.MaxValue) },
            { GameStartedKey, false }
        };

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = RequiredPlayers,
            IsOpen = true,
            IsVisible = true,
            BroadcastPropsChangeToAll = true,
            CustomRoomProperties = roomProperties
        };

        PhotonNetwork.JoinOrCreateRoom(
            roomName,
            options,
            TypedLobby.Default
        );
    }

    public override void OnJoinedRoom()
    {
        HandleJoinedRoom();
    }

    private void HandleJoinedRoom()
    {
        Debug.Log(
            $"Joined room '{PhotonNetwork.CurrentRoom.Name}' - " +
            $"{PhotonNetwork.CurrentRoom.PlayerCount}/{RequiredPlayers}"
        );

        /*GenerateArena();*/

        if (IsGameStarted())
            SpawnLocalTank();

        TryStartGame();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        TryStartGame();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        TryStartGame();
        CheckWinner();
    }

    private void TryStartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (IsGameStarted())
            return;

        if (PhotonNetwork.CurrentRoom.PlayerCount != RequiredPlayers)
            return;

        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable
            {
                { GameStartedKey, true }
            }
        );
    }

    public override void OnRoomPropertiesUpdate(
        Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey(GameStartedKey) &&
            (bool)propertiesThatChanged[GameStartedKey])
        {
            SpawnLocalTank();
        }

        if (propertiesThatChanged.ContainsKey(WinnerKey))
        {
            int winnerActor =
                (int)propertiesThatChanged[WinnerKey];

            Debug.Log($"Player {winnerActor} wins.");
        }
    }

    private bool IsGameStarted()
    {
        Hashtable properties =
            PhotonNetwork.CurrentRoom.CustomProperties;

        return properties.ContainsKey(GameStartedKey) &&
               (bool)properties[GameStartedKey];
    }

    private void SpawnLocalTank()
    {
        if (localTankSpawned)
            return;

        Player[] players = PhotonNetwork.PlayerList;

        int playerIndex = Array.FindIndex(
            players,
            player =>
                player.ActorNumber ==
                PhotonNetwork.LocalPlayer.ActorNumber
        );

        if (playerIndex < 0 ||
            playerIndex >= SpawnOffsets.Length)
            return;

        Vector3 position =
            spawnPoint.position +
            SpawnOffsets[playerIndex];

        Quaternion rotation =
            Quaternion.Euler(
                0f,
                SpawnRotations[playerIndex],
                0f
            );

        PhotonNetwork.Instantiate(
            tankPrefab.name,
            position,
            rotation
        );

        PhotonNetwork.LocalPlayer.SetCustomProperties(
            new Hashtable
            {
                { AliveKey, true }
            }
        );

        localTankSpawned = true;
    }

    private void GenerateArena()
    {
        if (arenaGenerated)
            return;

        Hashtable properties =
            PhotonNetwork.CurrentRoom.CustomProperties;

        if (!properties.ContainsKey(ArenaSeedKey))
            return;

        int seed = (int)properties[ArenaSeedKey];

        System.Random random =
            new System.Random(seed);

        Transform root =
            new GameObject("GeneratedArena").transform;

        Vector3 center = spawnPoint.position;

        CreateBlock(
            "Floor",
            center + new Vector3(0f, -0.25f, 0f),
            new Vector3(arenaSize, 0.5f, arenaSize),
            root
        );

        CreateArenaBorders(center, root);

        int created = 0;
        int attempts = 0;

        while (created < obstacleCount &&
               attempts < obstacleCount * 10)
        {
            attempts++;

            float halfSize = arenaSize * 0.5f - 2f;

            float x = Mathf.Lerp(
                -halfSize,
                halfSize,
                (float)random.NextDouble()
            );

            float z = Mathf.Lerp(
                -halfSize,
                halfSize,
                (float)random.NextDouble()
            );

            float width =
                Mathf.Lerp(
                    1.5f,
                    3.5f,
                    (float)random.NextDouble()
                );

            float depth =
                Mathf.Lerp(
                    1.5f,
                    3.5f,
                    (float)random.NextDouble()
                );

            Vector3 position =
                center + new Vector3(x, 1f, z);

            if (IsNearSpawnPoint(position))
                continue;

            CreateBlock(
                $"Obstacle_{created}",
                position,
                new Vector3(width, 2f, depth),
                root
            );

            created++;
        }

        arenaGenerated = true;
    }

    private void CreateArenaBorders(
        Vector3 center,
        Transform root)
    {
        float half = arenaSize * 0.5f;

        CreateBlock(
            "NorthWall",
            center + new Vector3(0f, 1f, half),
            new Vector3(arenaSize, 2f, 0.5f),
            root
        );

        CreateBlock(
            "SouthWall",
            center + new Vector3(0f, 1f, -half),
            new Vector3(arenaSize, 2f, 0.5f),
            root
        );

        CreateBlock(
            "EastWall",
            center + new Vector3(half, 1f, 0f),
            new Vector3(0.5f, 2f, arenaSize),
            root
        );

        CreateBlock(
            "WestWall",
            center + new Vector3(-half, 1f, 0f),
            new Vector3(0.5f, 2f, arenaSize),
            root
        );
    }

    private bool IsNearSpawnPoint(
        Vector3 worldPosition)
    {
        Vector3 localPosition =
            worldPosition - spawnPoint.position;

        foreach (Vector3 offset in SpawnOffsets)
        {
            Vector2 position =
                new Vector2(
                    localPosition.x,
                    localPosition.z
                );

            Vector2 spawn =
                new Vector2(
                    offset.x,
                    offset.z
                );

            if (Vector2.Distance(position, spawn) < 3f)
                return true;
        }

        return false;
    }

    private static void CreateBlock(
        string blockName,
        Vector3 position,
        Vector3 scale,
        Transform parent)
    {
        GameObject block =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        block.name = blockName;
        block.transform.SetParent(parent);
        block.transform.position = position;
        block.transform.localScale = scale;
    }

    public override void OnPlayerPropertiesUpdate(
        Player targetPlayer,
        Hashtable changedProps)
    {
        if (changedProps.ContainsKey(AliveKey))
            CheckWinner();
    }

    public override void OnPlayerLeftRoom(
        Player otherPlayer)
    {
        CheckWinner();
    }

    private void CheckWinner()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (!IsGameStarted())
            return;

        if (PhotonNetwork.CurrentRoom
            .CustomProperties
            .ContainsKey(WinnerKey))
        {
            return;
        }

        Player winner = null;
        int alivePlayers = 0;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (!player.CustomProperties
                .ContainsKey(AliveKey))
            {
                return;
            }

            bool alive =
                (bool)player.CustomProperties[AliveKey];

            if (!alive)
                continue;

            alivePlayers++;
            winner = player;
        }

        if (alivePlayers != 1)
            return;

        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable
            {
                { WinnerKey, winner.ActorNumber }
            }
        );
    }
}