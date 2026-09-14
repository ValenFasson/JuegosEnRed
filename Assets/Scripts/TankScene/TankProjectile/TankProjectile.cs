using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class TankProjectile : MonoBehaviourPun
{
    [SerializeField] private float speed = 14f;
    [SerializeField] private int damage = 30;
    [SerializeField] private float lifetime = 4f;

    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        body.useGravity = false;

        if (photonView.IsMine)
        {
            body.isKinematic = false;
            body.linearVelocity =
                transform.forward * speed;

            Invoke(
                nameof(DestroyProjectile),
                lifetime
            );
        }
        else
        {
            body.isKinematic = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine)
            return;

        if (other.TryGetComponent(
            out TankHealth health))
        {
            if (health.photonView.Owner !=
                photonView.Owner)
            {
                health.RequestDamage(damage);
            }
        }

        DestroyProjectile();
    }

    private void DestroyProjectile()
    {
        if (!photonView.IsMine)
            return;

        PhotonNetwork.Destroy(gameObject);
    }
}