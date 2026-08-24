using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhotonPlayerSpawner : MonoBehaviourPunCallbacks
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform spawnPoint;

    private GameObject localPlayerInstance;

    private void Start()
    {
        // Covers the case where this component is enabled after the room was already joined.
        if (PhotonNetwork.InRoom)
        {
            SpawnLocalPlayer();
        }
    }

    public override void OnJoinedRoom()
    {
        SpawnLocalPlayer();
    }

    public override void OnLeftRoom()
    {
        localPlayerInstance = null;
    }

    private void SpawnLocalPlayer()
    {
        if (localPlayerInstance != null)
        {
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[Photon] Player prefab is not assigned.", this);
            return;
        }

        if (playerPrefab.GetComponent<PhotonView>() == null)
        {
            Debug.LogError("[Photon] Player prefab root must contain a PhotonView.", playerPrefab);
            return;
        }

        if (Resources.Load<GameObject>(playerPrefab.name) == null)
        {
            Debug.LogError($"[Photon] '{playerPrefab.name}' was not found directly inside a Resources folder. PhotonNetwork.Instantiate needs a Resources-loadable prefab.", playerPrefab);
            return;
        }

        Vector3 position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        localPlayerInstance = PhotonNetwork.Instantiate(
            playerPrefab.name,
            position,
            rotation,
            0
        );
    }
}
