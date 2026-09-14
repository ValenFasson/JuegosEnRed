using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class Projectile : MonoBehaviourPun
{
    [SerializeField] private float speed = 20f;
    [SerializeField] private float lifeTime = 5f;

    private Rigidbody body;

    private bool IsLocalProjectile
    {
        get
        {
            return !PhotonNetwork.InRoom || photonView.IsMine;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (!IsLocalProjectile)
        {
            body.isKinematic = true;
            body.useGravity = false;
            return;
        }

        body.isKinematic = false;
        body.useGravity = false;

        body.linearVelocity =
            transform.forward * speed;

        Invoke(
            nameof(DestroyProjectile),
            lifeTime
        );
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsLocalProjectile)
            return;

        DestroyProjectile();
    }

    private void DestroyProjectile()
    {
        if (!IsLocalProjectile)
            return;

        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}