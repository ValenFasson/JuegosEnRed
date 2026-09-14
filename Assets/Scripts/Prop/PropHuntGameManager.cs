using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

using Hashtable =
    ExitGames.Client.Photon.Hashtable;

[RequireComponent(typeof(PhotonView))]
public sealed class PropHuntGameManager :
    MonoBehaviourPunCallbacks
{
    public static PropHuntGameManager Instance
    {
        get;
        private set;
    }

    public const string AliveKey = "alive";

    private const int ObjectiveCount =
        PropHuntRoundRules.RequiredButtons;

    private const double HideDuration =
        PropHuntRoundRules.HideDurationSeconds;

    private const string PhaseKey = "phase";
    private const string HunterActorKey = "hunter";
    private const string HideEndKey = "hideEnd";
    private const string WinnerKey = "winner";

    private static readonly string[] ButtonSlotKeys =
    {
        "buttonSlot0",
        "buttonSlot1",
        "buttonSlot2",
        "buttonSlot3",
        "buttonSlot4"
    };

    private static readonly string[] ButtonUsedKeys =
    {
        "buttonUsed0",
        "buttonUsed1",
        "buttonUsed2",
        "buttonUsed3",
        "buttonUsed4"
    };

    [Header("Players")]
    [SerializeField] private GameObject hunterPrefab;
    [SerializeField] private GameObject propPrefab;

    [Header("Spawns")]
    [SerializeField] private Transform hunterSpawn;
    [SerializeField] private Transform[] propSpawns;

    [Header("Buttons")]
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform[] buttonSpawnPoints;

    [Header("Results UI")]
    [SerializeField] private PropHuntResultUI resultUI;

    private GameObject localPlayerInstance;

    private NetworkPropController localPropController;
    private PropRoundUI localPropUI;
    private HunterRoundUI localHunterUI;

    private PropHuntObjectiveButton[] spawnedButtons;

    private int hunterActor = -1;
    private double hideEndTime;

    public GamePhase CurrentPhase
    {
        get;
        private set;
    } = GamePhase.Waiting;

    public GameWinner Winner
    {
        get;
        private set;
    } = GameWinner.None;

    private void Awake()
    {
        Instance = this;

        spawnedButtons =
            new PropHuntObjectiveButton[ObjectiveCount];
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogError(
                "PropHuntGameManager necesita estar dentro de una Room."
            );

            return;
        }

        ApplyRoomState();

        if (!PhotonNetwork.IsMasterClient)
            return;

        // Una vez que la escena Game fue cargada,
        // la partida puede comenzar con cualquier
        // cantidad de jugadores presentes.
        if (!PhotonNetwork.CurrentRoom
            .CustomProperties
            .ContainsKey(PhaseKey))
        {
            StartHidingPhase();
            return;
        }

        if (CurrentPhase == GamePhase.Waiting)
        {
            StartHidingPhase();
        }
    }

    private void Update()
    {
        if (CurrentPhase != GamePhase.Hiding)
            return;

        int remaining =
            GetRemainingHideSeconds();

        // UI del Hunter.
        if (localHunterUI != null)
        {
            localHunterUI.SetHideSeconds(
                remaining
            );
        }

        // UI del Prop.
        // IMPORTANTE: no debe depender de localHunterUI.
        if (localPropUI != null)
        {
            localPropUI.SetHideSeconds(
                remaining
            );
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        if (PhotonNetwork.Time >= hideEndTime)
        {
            StartPlayingPhase();
        }
    }

    private void StartHidingPhase()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null ||
            players.Length == 0)
        {
            Debug.LogError(
                "No hay jugadores en la Room."
            );

            return;
        }

        Player selectedHunter =
            players[
                UnityEngine.Random.Range(
                    0,
                    players.Length
                )
            ];

        double endTime =
            PhotonNetwork.Time +
            HideDuration;

        Hashtable properties =
            new Hashtable
            {
                {
                    PhaseKey,
                    (int)GamePhase.Hiding
                },
                {
                    HunterActorKey,
                    selectedHunter.ActorNumber
                },
                {
                    HideEndKey,
                    endTime
                },
                {
                    WinnerKey,
                    (int)GameWinner.None
                }
            };

        for (int i = 0;
             i < ObjectiveCount;
             i++)
        {
            properties[
                ButtonSlotKeys[i]
            ] = -1;

            properties[
                ButtonUsedKeys[i]
            ] = false;
        }

        // Una vez iniciada la partida,
        // no entran jugadores nuevos.
        PhotonNetwork.CurrentRoom.IsOpen =
            false;

        PhotonNetwork.CurrentRoom.IsVisible =
            false;

        PhotonNetwork.CurrentRoom
            .SetCustomProperties(
                properties
            );

        Debug.Log(
            $"[PropHunt] Comenzando partida con " +
            $"{players.Length} jugador(es). " +
            $"Hunter: Actor {selectedHunter.ActorNumber}"
        );
    }

    private void StartPlayingPhase()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase !=
            GamePhase.Hiding)
        {
            return;
        }

        if (buttonSpawnPoints == null ||
            buttonSpawnPoints.Length <
            ObjectiveCount)
        {
            Debug.LogError(
                "Se necesitan al menos 5 ButtonSpawnPoints."
            );

            return;
        }

        List<int> availableSlots =
            new List<int>();

        for (int i = 0;
             i < buttonSpawnPoints.Length;
             i++)
        {
            availableSlots.Add(i);
        }

        Hashtable properties =
            new Hashtable
            {
                {
                    PhaseKey,
                    (int)GamePhase.Playing
                }
            };

        for (int i = 0;
             i < ObjectiveCount;
             i++)
        {
            int randomListIndex =
                UnityEngine.Random.Range(
                    0,
                    availableSlots.Count
                );

            int selectedSlot =
                availableSlots[
                    randomListIndex
                ];

            availableSlots.RemoveAt(
                randomListIndex
            );

            properties[
                ButtonSlotKeys[i]
            ] = selectedSlot;

            properties[
                ButtonUsedKeys[i]
            ] = false;
        }

        PhotonNetwork.CurrentRoom
            .SetCustomProperties(
                properties
            );
    }

    private void FinishGame(
        GameWinner winner)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase == GamePhase.Finished || winner == GameWinner.None)
        {
            return;
        }

        PhotonNetwork.CurrentRoom
            .SetCustomProperties(
                new Hashtable
                {
                    {
                        PhaseKey,
                        (int)GamePhase.Finished
                    },
                    {
                        WinnerKey,
                        (int)winner
                    }
                },
                // El primer resultado confirmado no puede ser sobrescrito.
                new Hashtable { { PhaseKey, (int)CurrentPhase } }
            );
    }

    private void ApplyRoomState()
    {
        if (!PhotonNetwork.InRoom)
            return;

        Hashtable properties =
            PhotonNetwork.CurrentRoom
                .CustomProperties;

        if (properties.ContainsKey(
            PhaseKey))
        {
            CurrentPhase =
                (GamePhase)(int)
                    properties[PhaseKey];
        }

        if (properties.ContainsKey(
            HunterActorKey))
        {
            hunterActor =
                (int)properties[
                    HunterActorKey
                ];
        }

        if (properties.ContainsKey(
            HideEndKey))
        {
            hideEndTime =
                Convert.ToDouble(
                    properties[
                        HideEndKey
                    ]
                );
        }

        if (properties.ContainsKey(
            WinnerKey))
        {
            Winner =
                (GameWinner)(int)
                    properties[WinnerKey];
        }

        TrySpawnLocalPlayer();

        ApplyLocalPlayerState();

        if (resultUI != null)
            resultUI.ShowResult(CurrentPhase, Winner);

        if (CurrentPhase ==
                GamePhase.Playing ||
            CurrentPhase ==
                GamePhase.Finished)
        {
            SetupButtons();
        }
    }

    private void TrySpawnLocalPlayer()
    {
        if (localPlayerInstance != null)
            return;

        // Todavía no hay rol asignado.
        if (CurrentPhase ==
            GamePhase.Waiting)
        {
            return;
        }

        if (hunterActor <= 0)
            return;

        bool isHunter =
            PhotonNetwork.LocalPlayer
                .ActorNumber ==
            hunterActor;

        GameObject prefab =
            isHunter
                ? hunterPrefab
                : propPrefab;

        if (prefab == null)
        {
            Debug.LogError(
                "Falta asignar Hunter o Prop prefab."
            );

            return;
        }

        Transform spawn =
            isHunter
                ? hunterSpawn
                : GetLocalPropSpawn();

        Vector3 position =
            spawn != null
                ? spawn.position
                : Vector3.zero;

        Quaternion rotation =
            spawn != null
                ? spawn.rotation
                : Quaternion.identity;

        localPlayerInstance =
            PhotonNetwork.Instantiate(
                prefab.name,
                position,
                rotation
            );

        if (localPlayerInstance == null)
        {
            Debug.LogError(
                "No se pudo crear el jugador local."
            );

            return;
        }

        if (isHunter)
        {
            localHunterUI =
                localPlayerInstance
                    .GetComponent<
                        HunterRoundUI
                    >();

            if (localHunterUI == null)
            {
                Debug.LogWarning(
                    "El prefab Hunter no tiene HunterRoundUI en el root."
                );
            }

            Debug.Log(
                "[PropHunt] Jugador local creado como Hunter."
            );
        }
        else
        {
            localPropController =
                localPlayerInstance
                    .GetComponent<
                        NetworkPropController
                    >();

            localPropUI =
                localPlayerInstance
                    .GetComponentInChildren<
                        PropRoundUI
                    >(true);

            PhotonNetwork.LocalPlayer
                .SetCustomProperties(
                    new Hashtable
                    {
                        {
                            AliveKey,
                            true
                        }
                    }
                );

            Debug.Log(
                "[PropHunt] Jugador local creado como Prop."
            );
        }
    }

    private Transform GetLocalPropSpawn()
    {
        if (propSpawns == null ||
            propSpawns.Length == 0)
        {
            return null;
        }

        int propIndex = 0;

        foreach (Player player in
                 PhotonNetwork.PlayerList)
        {
            if (player.ActorNumber ==
                hunterActor)
            {
                continue;
            }

            if (player.ActorNumber ==
                PhotonNetwork.LocalPlayer
                    .ActorNumber)
            {
                break;
            }

            propIndex++;
        }

        propIndex =
            Mathf.Clamp(
                propIndex,
                0,
                propSpawns.Length - 1
            );

        return propSpawns[
            propIndex
        ];
    }

    private void ApplyLocalPlayerState()
    {
        if (CurrentPhase == GamePhase.Finished && localPlayerInstance != null)
        {
            NetworkFirstPersonController hunterController =
                localPlayerInstance.GetComponent<NetworkFirstPersonController>();
            PlayerGun gun = localPlayerInstance.GetComponent<PlayerGun>();
            Rigidbody body = localPlayerInstance.GetComponent<Rigidbody>();

            if (hunterController != null)
                hunterController.enabled = false;
            if (gun != null)
                gun.enabled = false;
            if (body != null)
                body.isKinematic = true;
        }

        // -------------------------------
        // PROP
        // -------------------------------

        if (localPropController != null)
        {
            bool hiding =
                CurrentPhase ==
                GamePhase.Hiding;

            localPropController
                .SetCanScale(
                    hiding
                );

            if (CurrentPhase ==
                GamePhase.Finished)
            {
                localPropController.enabled =
                    false;
            }
        }

        if (localPropUI != null)
        {
            if (CurrentPhase ==
                GamePhase.Hiding)
            {
                localPropUI.SetHiding(
                    GetRemainingHideSeconds()
                );
            }
            else
            {
                localPropUI.Hide();
            }
        }

        // -------------------------------
        // HUNTER
        // -------------------------------

        if (localHunterUI != null)
        {
            switch (CurrentPhase)
            {
                case GamePhase.Hiding:

                    localHunterUI.SetHiding(
                        GetRemainingHideSeconds()
                    );

                    break;

                case GamePhase.Playing:

                    localHunterUI.SetPlaying(
                        GetRemainingButtons()
                    );

                    break;

                case GamePhase.Finished:

                    localHunterUI.SetFinished();

                    break;
            }
        }
    }

    private int GetRemainingHideSeconds()
    {
        double remaining =
            hideEndTime -
            PhotonNetwork.Time;

        return Mathf.Max(
            0,
            Mathf.CeilToInt(
                (float)remaining
            )
        );
    }

    private void SetupButtons()
    {
        if (buttonPrefab == null ||
            buttonSpawnPoints == null)
        {
            return;
        }

        for (int i = 0;
             i < ObjectiveCount;
             i++)
        {
            int slot =
                GetRoomInt(
                    ButtonSlotKeys[i],
                    -1
                );

            if (slot < 0 ||
                slot >=
                buttonSpawnPoints.Length)
            {
                continue;
            }

            if (spawnedButtons[i] == null)
            {
                Transform spawn =
                    buttonSpawnPoints[
                        slot
                    ];

                GameObject button =
                    Instantiate(
                        buttonPrefab,
                        spawn.position,
                        spawn.rotation
                    );

                PropHuntObjectiveButton script =
                    button.GetComponent<
                        PropHuntObjectiveButton
                    >();

                if (script != null)
                {
                    script.Initialize(i);

                    spawnedButtons[i] =
                        script;
                }
            }

            if (spawnedButtons[i] != null)
            {
                bool used =
                    GetRoomBool(
                        ButtonUsedKeys[i],
                        false
                    );

                spawnedButtons[i]
                    .SetActivated(
                        used
                    );
            }
        }

        if (localHunterUI != null &&
            CurrentPhase ==
            GamePhase.Playing)
        {
            localHunterUI
                .SetObjectivesRemaining(
                    GetRemainingButtons()
                );
        }
    }

    public void RequestActivateButton(
        int objectiveIndex)
    {
        if (CurrentPhase !=
            GamePhase.Playing)
        {
            return;
        }

        if (PhotonNetwork.LocalPlayer
                .ActorNumber ==
            hunterActor)
        {
            return;
        }

        photonView.RPC(
            nameof(
                RpcRequestActivateButton
            ),
            RpcTarget.MasterClient,
            objectiveIndex
        );
    }

    [PunRPC]
    private void RpcRequestActivateButton(
        int objectiveIndex,
        PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase !=
            GamePhase.Playing)
        {
            return;
        }

        if (objectiveIndex < 0 ||
            objectiveIndex >=
            ObjectiveCount)
        {
            return;
        }

        if (info.Sender.ActorNumber ==
            hunterActor)
        {
            return;
        }

        if (!IsPlayerAlive(
            info.Sender))
        {
            return;
        }

        if (GetRoomBool(
            ButtonUsedKeys[
                objectiveIndex
            ],
            false))
        {
            return;
        }

        PhotonNetwork.CurrentRoom
            .SetCustomProperties(
                new Hashtable
                {
                    {
                        ButtonUsedKeys[
                            objectiveIndex
                        ],
                        true
                    }
                }
            );
    }

    private int GetRemainingButtons()
    {
        int remaining = 0;

        for (int i = 0;
             i < ObjectiveCount;
             i++)
        {
            if (!GetRoomBool(
                ButtonUsedKeys[i],
                false))
            {
                remaining++;
            }
        }

        return remaining;
    }

    private void CheckPropsWin()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase !=
            GamePhase.Playing)
        {
            return;
        }

        if (GetRemainingButtons() == 0)
        {
            FinishGame(
                GameWinner.Props
            );
        }
    }

    private void CheckHunterWin()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase !=
                GamePhase.Hiding &&
            CurrentPhase !=
                GamePhase.Playing)
        {
            return;
        }

        foreach (Player player in
                 PhotonNetwork.PlayerList)
        {
            if (player.ActorNumber ==
                hunterActor)
            {
                continue;
            }

            if (IsPlayerAlive(player))
            {
                return;
            }
        }

        FinishGame(
            GameWinner.Hunter
        );
    }

    private bool IsPlayerAlive(
        Player player)
    {
        if (player == null)
            return false;

        if (!player.CustomProperties
            .ContainsKey(AliveKey))
        {
            return false;
        }

        return
            (bool)player.CustomProperties[
                AliveKey
            ];
    }

    private int GetRoomInt(
        string key,
        int defaultValue)
    {
        Hashtable properties =
            PhotonNetwork.CurrentRoom
                .CustomProperties;

        if (!properties.ContainsKey(
            key))
        {
            return defaultValue;
        }

        return (int)properties[key];
    }

    private bool GetRoomBool(
        string key,
        bool defaultValue)
    {
        Hashtable properties =
            PhotonNetwork.CurrentRoom
                .CustomProperties;

        if (!properties.ContainsKey(
            key))
        {
            return defaultValue;
        }

        return (bool)properties[key];
    }

    public override void
        OnRoomPropertiesUpdate(
            Hashtable propertiesThatChanged)
    {
        ApplyRoomState();

        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase ==
            GamePhase.Playing)
        {
            CheckPropsWin();
            CheckHunterWin();
        }
    }

    public override void
        OnPlayerPropertiesUpdate(
            Player targetPlayer,
            Hashtable changedProps)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (!changedProps.ContainsKey(
            AliveKey))
        {
            return;
        }

        CheckHunterWin();
    }

    public override void
        OnPlayerLeftRoom(
            Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (CurrentPhase !=
                GamePhase.Hiding &&
            CurrentPhase !=
                GamePhase.Playing)
        {
            return;
        }

        if (otherPlayer.ActorNumber ==
            hunterActor)
        {
            FinishGame(
                GameWinner.Props
            );

            return;
        }

        CheckHunterWin();
    }

    public override void
        OnMasterClientSwitched(
            Player newMasterClient)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        ApplyRoomState();

        if (CurrentPhase ==
                GamePhase.Hiding &&
            PhotonNetwork.Time >=
                hideEndTime)
        {
            StartPlayingPhase();
        }

        if (CurrentPhase ==
            GamePhase.Playing)
        {
            CheckPropsWin();
            CheckHunterWin();
        }
    }
}