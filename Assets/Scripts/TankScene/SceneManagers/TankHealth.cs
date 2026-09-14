using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public sealed class TankHealth : MonoBehaviourPun
{
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;
    private bool dead;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void RequestDamage(int damage)
    {
        if (dead)
            return;

        photonView.RPC(
            nameof(RpcTakeDamage),
            RpcTarget.All,
            damage
        );
    }

    [PunRPC]
    private void RpcTakeDamage(int damage)
    {
        if (dead)
            return;

        currentHealth =
            Mathf.Max(
                0,
                currentHealth - damage
            );

        if (photonView.IsMine)
            Debug.Log($"Health: {currentHealth}");

        if (currentHealth > 0)
            return;

        dead = true;

        if (!photonView.IsMine)
            return;

        PhotonNetwork.LocalPlayer.SetCustomProperties(
            new Hashtable
            {
                { TankGame.AliveKey, false }
            }
        );

        PhotonNetwork.Destroy(gameObject);
    }
}