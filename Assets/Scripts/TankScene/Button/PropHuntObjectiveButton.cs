using Photon.Pun;
using UnityEngine;

public sealed class PropHuntObjectiveButton : MonoBehaviour
{
    private int objectiveIndex = -1;
    private bool activated;
    private bool activationRequested;

    private AudioSource audioSource;
    private Collider triggerCollider;
    private Renderer buttonRenderer;

    private void Awake()
    {
        audioSource =
            GetComponent<AudioSource>();

        triggerCollider =
            GetComponent<Collider>();

        buttonRenderer =
            GetComponentInChildren<Renderer>();
    }

    public void Initialize(int index)
    {
        objectiveIndex = index;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (activated)
            return;

        if (activationRequested)
            return;

        if (objectiveIndex < 0)
            return;

        // Solo un Prop puede activar el botón.
        NetworkPropController prop =
            other.GetComponentInParent<NetworkPropController>();

        if (prop == null)
            return;

        PhotonView propView =
            prop.GetComponent<PhotonView>();

        if (propView == null)
            return;

        // Solo el cliente dueño de ese Prop
        // manda la solicitud al Master.
        if (!propView.IsMine)
            return;

        if (PropHuntGameManager.Instance == null)
            return;

        activationRequested = true;

        PropHuntGameManager.Instance
            .RequestActivateButton(
                objectiveIndex
            );
    }

    public void SetActivated(bool value)
    {
        if (activated == value)
            return;

        activated = value;

        if (!activated)
        {
            activationRequested = false;
            return;
        }

        ChangeToYellow();

        PlayActivationSound();

        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }
    }

    private void ChangeToYellow()
    {
        if (buttonRenderer == null)
            return;

        buttonRenderer.material.color =
            Color.yellow;
    }

    private void PlayActivationSound()
    {
        if (audioSource == null)
            return;

        audioSource.Play();
    }
}