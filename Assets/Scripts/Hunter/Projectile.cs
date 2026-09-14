using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public sealed class Projectile : MonoBehaviourPun
{
    [Header("Projectile")]
    [SerializeField] private float speed = 25f;
    [SerializeField] private float lifeTime = 5f;

    private Rigidbody body;

    private bool hasHit;
    private float destroyTime;

    private bool IsLocalProjectile =>
        !PhotonNetwork.InRoom ||
        photonView.IsMine;

    private void Awake()
    {
        body =
            GetComponent<Rigidbody>();

        body.useGravity = false;

        // Evita que proyectiles r�pidos atraviesen
        // colliders entre frames.
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;

        body.interpolation =
            RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        destroyTime =
            Time.time + lifeTime;

        // En red solamente el owner calcula
        // la f�sica del proyectil.
        if (PhotonNetwork.InRoom &&
            !photonView.IsMine)
        {
            body.isKinematic = true;
            return;
        }

        body.isKinematic = false;

        body.linearVelocity =
            transform.forward * speed;
    }

    private void Update()
    {
        if (!IsLocalProjectile)
            return;

        if (Time.time >= destroyTime)
        {
            DestroyProjectile();
        }
    }

    private void OnCollisionEnter(
        Collision collision)
    {
        if (!IsLocalProjectile)
            return;

        if (collision == null)
            return;

        HandleHit(
            collision.collider
        );
    }

    private void OnTriggerEnter(
        Collider other)
    {
        if (!IsLocalProjectile)
            return;

        HandleHit(other);
    }

    private void HandleHit(
        Collider hitCollider)
    {
        if (hasHit)
            return;

        if (hitCollider == null)
            return;

        hasHit = true;

        PropVisual prop =
            hitCollider
                .GetComponentInParent<PropVisual>();

        if (prop != null)
        {
            prop.RequestElimination();
        }

        DestroyProjectile();
    }

    private void DestroyProjectile()
    {
        if (PhotonNetwork.InRoom)
        {
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(
                    gameObject
                );
            }

            return;
        }

        Destroy(gameObject);
    }
}