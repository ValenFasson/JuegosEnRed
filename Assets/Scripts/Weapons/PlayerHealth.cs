using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Mirrored health state. The Master Client calculates damage and publishes the
/// resulting value; all clients only apply the confirmed health value.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public sealed class PlayerHealth : MonoBehaviourPunCallbacks
{
    [SerializeField, Min(1f)] private float maximumHealth = 100f;
    [SerializeField] private Transform headHitbox;
    [SerializeField] private UnityEvent<float> onHealthChanged;
    [SerializeField] private UnityEvent onDied;

    private float currentHealth;
    private string healthPropertyKey;

    public float CurrentHealth => currentHealth;
    public float MaximumHealth => maximumHealth;

    private void Awake()
    {
        currentHealth = maximumHealth;
    }

    private void Start()
    {
        if (!PhotonNetwork.InRoom)
            return;

        healthPropertyKey = GetHealthPropertyKey();
        ApplyRoomHealthIfPresent();

        if (PhotonNetwork.IsMasterClient && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(healthPropertyKey))
            PublishHealth(currentHealth, -1, -1);
    }

    public bool IsHeadshot(Collider hitCollider)
    {
        if (headHitbox == null || hitCollider == null)
            return hitCollider != null && hitCollider.transform.name == "Head";

        return hitCollider.transform == headHitbox || hitCollider.transform.IsChildOf(headHitbox);
    }

    /// <summary>Called on the Master Client's copy after its hitscan finds this player.</summary>
    public void ApplyDamageFromMaster(float damage, int attackerActor, int shotSequence)
    {
        if (damage <= 0f || (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient))
            return;

        float nextHealth = Mathf.Max(0f, currentHealth - damage);
        if (Mathf.Approximately(nextHealth, currentHealth))
            return;

        if (!PhotonNetwork.InRoom)
        {
            SetHealth(nextHealth);
            return;
        }

        PublishHealth(nextHealth, attackerActor, shotSequence);
    }

    [PunRPC]
    private void RpcSetHealth(float health, int attackerActor, int shotSequence, PhotonMessageInfo info)
    {
        if (PhotonNetwork.InRoom && (info.Sender == null || !info.Sender.IsMasterClient))
            return;

        SetHealth(health);
    }

    public override void OnRoomPropertiesUpdate(Hashtable changedProperties)
    {
        if (string.IsNullOrEmpty(healthPropertyKey) || !changedProperties.ContainsKey(healthPropertyKey))
            return;

        ApplyRoomHealthIfPresent();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // Room Custom Properties outlive the Master Client that wrote them.
        ApplyRoomHealthIfPresent();
    }

    private void PublishHealth(float health, int attackerActor, int shotSequence)
    {
        currentHealth = health;
        if (PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { healthPropertyKey, health }
            });
        }

        photonView.RPC(nameof(RpcSetHealth), RpcTarget.AllViaServer, health, attackerActor, shotSequence);
    }

    private void ApplyRoomHealthIfPresent()
    {
        if (PhotonNetwork.CurrentRoom == null || string.IsNullOrEmpty(healthPropertyKey))
            return;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(healthPropertyKey, out object value) && value is float health)
            SetHealth(health);
    }

    private string GetHealthPropertyKey()
    {
        int actorNumber = photonView.Owner != null ? photonView.Owner.ActorNumber : photonView.OwnerActorNr;
        return "player_health_" + actorNumber;
    }

    private void SetHealth(float health)
    {
        float oldHealth = currentHealth;
        currentHealth = Mathf.Clamp(health, 0f, maximumHealth);
        onHealthChanged?.Invoke(currentHealth);

        if (oldHealth > 0f && currentHealth <= 0f)
            onDied?.Invoke();
    }
}
