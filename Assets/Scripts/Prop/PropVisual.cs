using Photon.Pun;
using UnityEngine;

public sealed class PropVisual : MonoBehaviourPun
{
    [SerializeField] private Transform visualsRoot;

    private const float MinScale = 0.5f;
    private const float MaxScale = 2f;

    private GameObject[] variants;

    private int currentVariant = -1;
    private float currentScale = 1f;

    private void Awake()
    {
        if (visualsRoot == null)
            return;

        variants =
            new GameObject[visualsRoot.childCount];

        for (int i = 0; i < visualsRoot.childCount; i++)
        {
            variants[i] =
                visualsRoot.GetChild(i).gameObject;

            variants[i].SetActive(false);
        }
    }

    private void Start()
    {
        if (variants == null || variants.Length == 0)
        {
            Debug.LogError(
                "El Prop no tiene variantes visuales.",
                this
            );

            return;
        }

        if (!PhotonNetwork.InRoom)
        {
            int randomIndex =
                Random.Range(0, variants.Length);

            ApplyVariant(randomIndex);
            return;
        }

        if (!photonView.IsMine)
            return;

        int selectedVariant =
            Random.Range(0, variants.Length);

        photonView.RPC(
            nameof(RpcSetVariant),
            RpcTarget.AllBuffered,
            selectedVariant
        );
    }

    [PunRPC]
    private void RpcSetVariant(int variantIndex)
    {
        ApplyVariant(variantIndex);
    }

    private void ApplyVariant(int variantIndex)
    {
        if (variants == null)
            return;

        if (variantIndex < 0 ||
            variantIndex >= variants.Length)
        {
            return;
        }

        for (int i = 0; i < variants.Length; i++)
        {
            variants[i].SetActive(
                i == variantIndex
            );
        }

        currentVariant = variantIndex;
    }

    public void RequestScaleChange(float amount)
    {
        if (PhotonNetwork.InRoom &&
            !photonView.IsMine)
        {
            return;
        }

        float newScale =
            Mathf.Clamp(
                currentScale + amount,
                MinScale,
                MaxScale
            );

        if (Mathf.Approximately(
            newScale,
            currentScale))
        {
            return;
        }

        if (PhotonNetwork.InRoom)
        {
            photonView.RPC(
                nameof(RpcSetScale),
                RpcTarget.AllBuffered,
                newScale
            );
        }
        else
        {
            ApplyScale(newScale);
        }
    }

    [PunRPC]
    private void RpcSetScale(float newScale)
    {
        ApplyScale(newScale);
    }

    private void ApplyScale(float newScale)
    {
        if (visualsRoot == null)
            return;

        currentScale = newScale;

        visualsRoot.localScale =
            Vector3.one * currentScale;
    }

    public void RequestElimination()
    {
        if (!PhotonNetwork.InRoom)
        {
            Destroy(gameObject);
            return;
        }

        photonView.RPC(
            nameof(RpcEliminate),
            RpcTarget.All
        );
    }

    [PunRPC]
    private void RpcEliminate()
    {
        if (!photonView.IsMine)
            return;

        PhotonNetwork.Destroy(gameObject);
    }
}