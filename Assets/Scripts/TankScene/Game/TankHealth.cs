using Photon.Pun;
using UnityEngine;

// Presentation of the durable player state. Only TankGame's Master resolves an impact.
public class TankHealth : MonoBehaviourPunCallbacks
{
    private TankController tank;
    private Renderer[] renderers;
    private Collider[] colliders;

    private void Awake()
    {
        tank = GetComponent<TankController>();
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    private void Start() => ApplyState();

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changed) => ApplyState();

    private void ApplyState()
    {
        bool visible = PhotonNetwork.InRoom && tank != null && tank.RoundId == TankGame.CurrentRound && TankGame.IsParticipant(tank.ActorNumber) && TankGame.GetPlayerState(tank.ActorNumber) == PropHuntPlayerState.Alive;
        foreach (Renderer item in renderers)
            item.enabled = visible;
        foreach (Collider item in colliders)
            item.enabled = visible;
        // Keep PhotonView until round cleanup: rejoining must not resurrect an eliminated prop.
    }
}
