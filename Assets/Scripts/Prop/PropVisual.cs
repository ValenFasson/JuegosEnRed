using Photon.Pun;
using UnityEngine;
using Hashtable =
    ExitGames.Client.Photon.Hashtable;

public sealed class PropVisual : MonoBehaviourPun, IPunObservable
{
    [SerializeField] private Transform visualsRoot;

    private const float MinScale = 0.5f;
    private const float MaxScale = 1.5f;
    private const float NetworkSmooth = 12f;

    private GameObject[] variants;

    private int currentVariant = -1;

    private float currentScale = 1f;
    private float networkScale = 1f;

    private float visualYaw;
    private float networkVisualYaw;

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
        if (variants == null ||
            variants.Length == 0)
        {
            Debug.LogError(
                "El Prop no tiene variantes visuales.",
                this
            );

            return;
        }

        // Prueba local sin Photon.
        if (!PhotonNetwork.InRoom)
        {
            int randomIndex =
                Random.Range(
                    0,
                    variants.Length
                );

            ApplyVariant(randomIndex);
            return;
        }

        // En Photon solamente el owner elige
        // qué variante corresponde.
        if (!photonView.IsMine)
            return;

        int selectedVariant =
            Random.Range(
                0,
                variants.Length
            );

        photonView.RPC(
            nameof(RpcSetVariant),
            RpcTarget.AllBuffered,
            selectedVariant
        );
    }

    private void Update()
    {
        if (!PhotonNetwork.InRoom)
            return;

        // El owner modifica directamente sus visuales.
        if (photonView.IsMine)
            return;

        if (visualsRoot == null)
            return;

        Quaternion targetRotation =
            Quaternion.Euler(
                0f,
                networkVisualYaw,
                0f
            );

        visualsRoot.localRotation =
            Quaternion.Slerp(
                visualsRoot.localRotation,
                targetRotation,
                NetworkSmooth * Time.deltaTime
            );

        Vector3 targetScale =
            Vector3.one * networkScale;

        visualsRoot.localScale =
            Vector3.Lerp(
                visualsRoot.localScale,
                targetScale,
                NetworkSmooth * Time.deltaTime
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

    public void SetVisualRotation(float yaw)
    {
        visualYaw = yaw;

        if (visualsRoot == null)
            return;

        visualsRoot.localRotation =
            Quaternion.Euler(
                0f,
                visualYaw,
                0f
            );
    }

    public void ChangeScale(float amount)
    {
        if (PhotonNetwork.InRoom &&
            !photonView.IsMine)
        {
            return;
        }

        currentScale =
            Mathf.Clamp(
                currentScale + amount,
                MinScale,
                MaxScale
            );

        ApplyScale(currentScale);
    }

    private void ApplyScale(float newScale)
    {
        if (visualsRoot == null)
            return;

        currentScale =
            Mathf.Clamp(
                newScale,
                MinScale,
                MaxScale
            );

        visualsRoot.localScale =
            Vector3.one * currentScale;
    }

    public void RequestElimination()
    {
        // Prueba local.
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
            gameObject
        );
    }

    public void OnPhotonSerializeView(
        PhotonStream stream,
        PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(visualYaw);
            stream.SendNext(currentScale);
        }
        else
        {
            networkVisualYaw =
                (float)stream.ReceiveNext();

            networkScale =
                (float)stream.ReceiveNext();
        }
    }
}