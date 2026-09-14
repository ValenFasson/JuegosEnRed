using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

public sealed class PropVisual :
    MonoBehaviourPun,
    IPunObservable
{
    [Header("Visuals")]
    [SerializeField] private Transform visualsRoot;

    private readonly List<GameObject> visuals =
        new List<GameObject>();

    private int selectedVisualIndex = -1;

    private float visualYaw;
    private float visualScale = 1f;

    private bool eliminated;

    private void Awake()
    {
        CacheVisuals();
    }

    private void Start()
    {
        // Testing sin Photon.
        if (!PhotonNetwork.InRoom)
        {
            SelectRandomVisualLocal();
            ApplyVisualTransform();
            return;
        }

        // Solo el owner decide qué visual toca.
        if (photonView.IsMine)
        {
            int randomIndex =
                Random.Range(
                    0,
                    visuals.Count
                );

            photonView.RPC(
                nameof(RpcSetVisual),
                RpcTarget.AllBuffered,
                randomIndex
            );
        }
    }

    private void CacheVisuals()
    {
        visuals.Clear();

        if (visualsRoot == null)
        {
            Debug.LogError(
                "PropVisual: falta asignar Visuals Root.",
                this
            );

            return;
        }

        for (int i = 0;
             i < visualsRoot.childCount;
             i++)
        {
            visuals.Add(
                visualsRoot
                    .GetChild(i)
                    .gameObject
            );
        }

        if (visuals.Count == 0)
        {
            Debug.LogError(
                "PropVisual: Visuals Root no tiene variantes.",
                this
            );
        }
    }

    private void SelectRandomVisualLocal()
    {
        if (visuals.Count == 0)
            return;

        selectedVisualIndex =
            Random.Range(
                0,
                visuals.Count
            );

        ApplySelectedVisual();
    }

    [PunRPC]
    private void RpcSetVisual(
        int index)
    {
        if (index < 0 ||
            index >= visuals.Count)
        {
            return;
        }

        selectedVisualIndex = index;

        ApplySelectedVisual();
    }

    private void ApplySelectedVisual()
    {
        for (int i = 0;
             i < visuals.Count;
             i++)
        {
            if (visuals[i] == null)
                continue;

            visuals[i].SetActive(
                i == selectedVisualIndex
            );
        }
    }

    // --------------------------------------------------
    // ROTATION
    // --------------------------------------------------

    public void AddVisualRotation(
        float yawDelta)
    {
        if (!CanControlVisual())
            return;

        visualYaw += yawDelta;

        ApplyVisualTransform();
    }

    public void SetVisualRotation(
        float yaw)
    {
        if (!CanControlVisual())
            return;

        visualYaw = yaw;

        ApplyVisualTransform();
    }

    // --------------------------------------------------
    // SCALE
    // --------------------------------------------------

    public void SetVisualScale(
        float scale)
    {
        if (!CanControlVisual())
            return;

        visualScale =
            PropHuntRoundRules.ClampPropScale(
                scale
            );

        ApplyVisualTransform();
    }

    public float GetVisualScale()
    {
        return visualScale;
    }

    private void ApplyVisualTransform()
    {
        if (visualsRoot == null)
            return;

        visualsRoot.localRotation =
            Quaternion.Euler(
                0f,
                visualYaw,
                0f
            );

        visualsRoot.localScale =
            Vector3.one *
            visualScale;
    }

    private bool CanControlVisual()
    {
        return
            !PhotonNetwork.InRoom ||
            photonView.IsMine;
    }


    public void ChangeScale(float delta)
    {
        if (!CanControlVisual())
            return;

        visualScale += delta;

        visualScale =
            PropHuntRoundRules.ClampPropScale(
                visualScale
            );

        ApplyVisualTransform();
    }

    // --------------------------------------------------
    // ELIMINATION
    // --------------------------------------------------

    public void RequestElimination()
    {
        if (eliminated)
            return;

        // Testing offline.
        if (!PhotonNetwork.InRoom)
        {
            eliminated = true;

            Destroy(
                photonView != null
                    ? photonView.gameObject
                    : gameObject
            );

            return;
        }

        if (photonView.Owner == null)
        {
            Debug.LogWarning(
                "PropVisual: el Prop no tiene owner.",
                this
            );

            return;
        }

        // La eliminación la ejecuta el cliente
        // dueño del Prop.
        photonView.RPC(
            nameof(RpcEliminate),
            photonView.Owner
        );
    }

    [PunRPC]
    private void RpcEliminate()
    {
        // Solo el owner puede destruir correctamente
        // su objeto PhotonNetwork.Instantiate.
        if (!photonView.IsMine)
            return;

        if (eliminated)
            return;

        eliminated = true;

        PhotonNetwork.LocalPlayer
            .SetCustomProperties(
                new Hashtable
                {
                    {
                        PropHuntGameManager.AliveKey,
                        false
                    }
                }
            );

        PhotonNetwork.Destroy(
            photonView.gameObject
        );
    }

    // --------------------------------------------------
    // NETWORK VISUAL SYNC
    // --------------------------------------------------

    public void OnPhotonSerializeView(
        PhotonStream stream,
        PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(
                visualYaw
            );

            stream.SendNext(
                visualScale
            );
        }
        else
        {
            visualYaw =
                (float)stream.ReceiveNext();

            visualScale =
                (float)stream.ReceiveNext();

            visualScale =
                PropHuntRoundRules.ClampPropScale(
                    visualScale
                );

            ApplyVisualTransform();
        }
    }
}