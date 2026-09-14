using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
public sealed class TankController : MonoBehaviourPunCallbacks, IOnPhotonViewControllerChange, IPunInstantiateMagicCallback
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 120f;

    [Header("Shooting")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float fireCooldown = 0.4f;
    [SerializeField] private float projectileSpawnDistance = 1.2f;

    private Rigidbody body;

    private float moveInput;
    private float turnInput;
    private double nextFireTime;
    private bool simulating;
    private bool despawnRequested;
    private bool shotPending;
    private int requestedRound;
    private int requestedSequence;
    private Vector3 requestedDirection;
    private float shotRequestedAt;
    private float nextShotRetry;
    private int requestedButton = -1;
    private int buttonRequestRound;
    private float buttonRequestedAt;
    private float nextButtonRetry;
    private static readonly List<TankController> Tanks = new List<TankController>();

    public int RoundId { get; private set; } = -1;
    public int ActorNumber { get; private set; }
    public float FireCooldown => Mathf.Clamp(fireCooldown, 0.1f, 5f);
    public float ProjectileSpawnDistance => Mathf.Clamp(projectileSpawnDistance, 0.5f, 3f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Tanks.Clear();

    public override void OnEnable()
    {
        base.OnEnable();
        Tanks.Add(this);
        photonView.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        photonView.RemoveCallbackTarget(this);
        Tanks.Remove(this);
        base.OnDisable();
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void Start()
    {
        InitializeIdentity();
        ApplyPhysicsState();
    }

    public void OnPhotonInstantiate(PhotonMessageInfo info) => InitializeIdentity();

    private void InitializeIdentity()
    {
        object[] data = photonView.InstantiationData;
        if (data != null && data.Length == 3 && data[0] is int protocol &&
            protocol == PropHuntRoundRules.Protocol && data[1] is int round &&
            data[2] is int actor && actor == photonView.CreatorActorNr)
        {
            RoundId = round;
            ActorNumber = actor;
        }
    }

    public static TankController FindForActor(int actor, int round)
    {
        TankController result = null;
        foreach (TankController tank in Tanks)
        {
            if (tank != null && !tank.despawnRequested && tank.ActorNumber == actor && tank.RoundId == round &&
                (result == null || tank.photonView.ViewID < result.photonView.ViewID))
                result = tank;
        }
        return result;
    }

    public bool TryGetProjectileConfiguration(out TankProjectile projectile)
    {
        projectile = projectilePrefab != null ? projectilePrefab.GetComponent<TankProjectile>() : null;
        return projectile != null && projectilePrefab.GetComponent<PhotonView>() != null;
    }

    public void RestorePose(Vector3 position, Quaternion rotation)
    {
        if (!PropHuntRoundRules.IsFinite(position.x) || !PropHuntRoundRules.IsFinite(position.y) ||
            !PropHuntRoundRules.IsFinite(position.z))
            return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.position = position;
        body.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    private bool CanSimulateLocalAvatar => PhotonNetwork.InRoom &&
        ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber &&
        photonView.OwnerActorNr == ActorNumber && !PhotonNetwork.LocalPlayer.IsInactive &&
        RoundId == TankGame.CurrentRound && TankGame.CanLocalPlayerMove();

    private void ApplyPhysicsState()
    {
        bool shouldSimulate = CanSimulateLocalAvatar;
        if (simulating == shouldSimulate)
            return;
        if (!shouldSimulate && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        simulating = shouldSimulate;
        body.isKinematic = !simulating;
        body.useGravity = simulating;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    public void OnControllerChange(Player newController, Player previousController) => ApplyPhysicsState();

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changed) => ApplyPhysicsState();

    public override void OnDisconnected(DisconnectCause cause)
    {
        ApplyPhysicsState();
        shotPending = false;
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && RoundId >= 0 && !despawnRequested && photonView.IsMine &&
            (TankGame.Phase == GamePhase.Waiting || RoundId != TankGame.CurrentRound ||
             FindForActor(ActorNumber, RoundId) != this))
        {
            // PUN permits Destroy only for the owner/controller, not for any active remote owner.
            despawnRequested = true;
            PhotonNetwork.Destroy(gameObject);
            return;
        }
        ApplyPhysicsState();
        if (!CanSimulateLocalAvatar)
        {
            moveInput = 0f;
            turnInput = 0f;
            shotPending = false;
            requestedButton = -1;
            return;
        }

        bool usingUI = EventSystem.current != null && EventSystem.current.sendNavigationEvents &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>() != null;
        if (usingUI)
        {
            moveInput = turnInput = 0f;
        }
        else
        {
            ReadInput();
        }
        UpdateButtonInteraction();

        if (!usingUI && Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame &&
            TankGame.CanLocalPlayerShoot() &&
            !shotPending && PhotonNetwork.Time >= nextFireTime)
        {
            Shoot();
        }
        if (shotPending)
        {
            if (TankGame.LastAcceptedShot >= requestedSequence ||
                requestedRound != TankGame.CurrentRound ||
                Time.realtimeSinceStartup - shotRequestedAt > 3f)
                shotPending = false;
            else if (Time.realtimeSinceStartup >= nextShotRetry)
                SendShotRequest();
        }
    }

    private void UpdateButtonInteraction()
    {
        if (!TankGame.CanLocalPlayerActivateButtons())
        {
            requestedButton = -1;
            return;
        }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && requestedButton < 0 &&
            !(EventSystem.current != null && EventSystem.current.sendNavigationEvents &&
              EventSystem.current.currentSelectedGameObject != null &&
              EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>() != null))
        {
            PropHuntObjectiveButton nearby = PropHuntObjectiveButton.FindNearby(transform.position);
            if (nearby != null)
            {
                requestedButton = nearby.ButtonId;
                buttonRequestRound = RoundId;
                buttonRequestedAt = Time.realtimeSinceStartup;
                SendButtonRequest();
            }
        }
        if (requestedButton < 0)
            return;
        PropHuntObjectiveButton button = PropHuntObjectiveButton.Find(requestedButton);
        if (TankGame.IsButtonActivated(requestedButton) || buttonRequestRound != TankGame.CurrentRound ||
            Time.realtimeSinceStartup - buttonRequestedAt > 3f || button == null ||
            (transform.position - button.transform.position).sqrMagnitude >
                PropHuntRoundRules.ButtonActivationRange * PropHuntRoundRules.ButtonActivationRange)
            requestedButton = -1;
        else if (Time.realtimeSinceStartup >= nextButtonRetry)
            SendButtonRequest();
    }

    private void SendButtonRequest()
    {
        nextButtonRetry = Time.realtimeSinceStartup + 0.5f;
        photonView.RPC(nameof(RpcRequestActivateButton), RpcTarget.MasterClient,
            buttonRequestRound, requestedButton);
    }

    [PunRPC]
    private void RpcRequestActivateButton(int round, int buttonId, PhotonMessageInfo info)
    {
        if (TankGame.Instance != null)
            TankGame.Instance.TryActivateButton(this, info.Sender, round, buttonId);
    }

    private void FixedUpdate()
    {
        ApplyPhysicsState();
        if (!CanSimulateLocalAvatar)
            return;

        Move();
        Rotate();
    }

    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            moveInput = 0f;
            turnInput = 0f;
            return;
        }

        moveInput = 0f;
        turnInput = 0f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            moveInput += 1f;

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            moveInput -= 1f;

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            turnInput += 1f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            turnInput -= 1f;
    }

    private void Move()
    {
        Vector3 movement =
            transform.forward *
            moveInput *
            moveSpeed *
            Time.fixedDeltaTime;

        body.MovePosition(body.position + movement);
    }

    private void Rotate()
    {
        Quaternion rotation = Quaternion.Euler(
            0f,
            turnInput * turnSpeed * Time.fixedDeltaTime,
            0f
        );

        body.MoveRotation(body.rotation * rotation);
    }

    private void Shoot()
    {
        nextFireTime = PhotonNetwork.Time + FireCooldown;
        requestedRound = RoundId;
        requestedSequence = TankGame.LastAcceptedShot + 1;
        requestedDirection = transform.forward;
        shotRequestedAt = Time.realtimeSinceStartup;
        shotPending = true;
        SendShotRequest();
    }

    private void SendShotRequest()
    {
        nextShotRetry = Time.realtimeSinceStartup + 0.5f;
        photonView.RPC(nameof(RpcRequestShoot), RpcTarget.MasterClient,
            requestedRound, requestedSequence, requestedDirection);
    }

    [PunRPC]
    private void RpcRequestShoot(int round, int sequence, Vector3 direction, PhotonMessageInfo info)
    {
        if (TankGame.Instance != null)
            TankGame.Instance.TryAcceptShot(this, info.Sender, round, sequence, direction);
    }
}
