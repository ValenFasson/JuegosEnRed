using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class TankProjectile : MonoBehaviourPunCallbacks, IOnPhotonViewControllerChange
{
    [SerializeField] private float speed = 14f;
    [SerializeField] private float lifetime = 4f;

    private Rigidbody body;
    private float networkSpeed;
    private bool initialized;
    private bool awaitingImpact;
    private int impactActor;
    private bool simulationActive;
    private bool destroyRequested;
    private Vector3 previousPosition;
    private SphereCollider projectileCollider;
    private readonly RaycastHit[] sweepHits = new RaycastHit[16];
    private static readonly List<TankProjectile> Projectiles = new List<TankProjectile>();

    public float Speed => Mathf.Clamp(speed, 1f, 50f);
    public float Lifetime => Mathf.Clamp(lifetime, 0.5f, 15f);
    public int RoundId { get; private set; } = -1;
    public int ShotSequence { get; private set; }
    public int HunterActor { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Projectiles.Clear();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        projectileCollider = GetComponent<SphereCollider>();
        body.useGravity = false;
        body.isKinematic = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        Projectiles.Add(this);
        photonView.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        photonView.RemoveCallbackTarget(this);
        Projectiles.Remove(this);
        base.OnDisable();
    }

    private void Start()
    {
        object[] data = photonView.InstantiationData;
        if (photonView.IsRoomView && data != null && data.Length == 6 &&
            data[0] is int protocol && protocol == PropHuntRoundRules.Protocol &&
            data[1] is int round && data[2] is int sequence && data[3] is int hunter &&
            data[4] is float velocity && data[5] is double)
        {
            RoundId = round;
            ShotSequence = sequence;
            HunterActor = hunter;
            networkSpeed = velocity;
            initialized = true;
        }
        ApplySimulationState();
        previousPosition = body.position;
    }

    public static TankProjectile FindShot(int round, int sequence)
    {
        TankProjectile result = null;
        foreach (TankProjectile shot in Projectiles)
        {
            if (shot != null && shot.initialized && !shot.destroyRequested &&
                shot.RoundId == round && shot.ShotSequence == sequence &&
                (result == null || shot.photonView.ViewID < result.photonView.ViewID))
                result = shot;
        }
        return result;
    }

    public static void RemoveDuplicateShots(int round, int sequence, TankProjectile canonical)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;
        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            TankProjectile shot = Projectiles[i];
            if (shot != null && shot != canonical && shot.RoundId == round && shot.ShotSequence == sequence)
                shot.DestroyNetworkObject();
        }
    }

    private void Update()
    {
        ApplySimulationState();
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;
        if (!initialized || !TankGame.IsShotActive(RoundId, ShotSequence))
        {
            DestroyNetworkObject();
            return;
        }
        if (awaitingImpact && TankGame.Instance != null)
            TankGame.Instance.TryResolveImpact(this, impactActor);
    }

    private void ApplySimulationState()
    {
        bool shouldSimulate = initialized && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient &&
                              TankGame.IsShotActive(RoundId, ShotSequence) && !awaitingImpact;
        if (shouldSimulate == simulationActive)
            return;
        if (!shouldSimulate && !body.isKinematic)
            body.linearVelocity = Vector3.zero;
        simulationActive = shouldSimulate;
        body.isKinematic = !simulationActive;
        if (simulationActive)
            body.linearVelocity = transform.forward * networkSpeed;
    }

    public void OnControllerChange(Player newController, Player previousController)
    {
        previousPosition = body.position;
        ApplySimulationState();
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changed) => ApplySimulationState();

    public override void OnDisconnected(DisconnectCause cause) => ApplySimulationState();

    private void OnTriggerEnter(Collider other)
    {
        if (!simulationActive || awaitingImpact || FindShot(RoundId, ShotSequence) != this)
            return;
        if (other.GetComponentInParent<TankProjectile>() != null)
            return;
        TankController target = other.GetComponentInParent<TankController>();
        if (target != null && target.ActorNumber == HunterActor)
            return;
        impactActor = target != null && target.RoundId == RoundId ? target.ActorNumber : 0;
        awaitingImpact = true;
        ApplySimulationState();
        if (TankGame.Instance != null)
            TankGame.Instance.TryResolveImpact(this, impactActor);
    }

    private void OnTriggerStay(Collider other) => OnTriggerEnter(other);

    private void FixedUpdate()
    {
        if (simulationActive && !awaitingImpact)
        {
            Vector3 travel = body.position - previousPosition;
            float distance = travel.magnitude;
            if (distance > 0.001f)
            {
                Vector3 scale = transform.lossyScale;
                float radius = projectileCollider != null ? projectileCollider.radius *
                    Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) : 0.05f;
                int hits = Physics.SphereCastNonAlloc(previousPosition, radius, travel / distance,
                    sweepHits, distance, ~0, QueryTriggerInteraction.Collide);
                Collider nearest = null;
                float nearestDistance = float.MaxValue;
                for (int i = 0; i < hits; i++)
                {
                    Collider candidate = sweepHits[i].collider;
                    TankController tank = candidate.GetComponentInParent<TankController>();
                    if (candidate.GetComponentInParent<TankProjectile>() != null ||
                        (tank != null && tank.ActorNumber == HunterActor))
                        continue;
                    if (sweepHits[i].distance < nearestDistance)
                    {
                        nearest = candidate;
                        nearestDistance = sweepHits[i].distance;
                    }
                }
                if (nearest != null)
                    OnTriggerEnter(nearest);
            }
        }
        previousPosition = body.position;
    }

    private void DestroyNetworkObject()
    {
        if (destroyRequested || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;
        destroyRequested = true;
        PhotonNetwork.Destroy(gameObject);
    }
}
