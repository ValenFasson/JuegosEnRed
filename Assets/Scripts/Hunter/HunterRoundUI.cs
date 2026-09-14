using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HunterRoundUI : MonoBehaviour
{
    [Header("Hiding")]
    [SerializeField] private GameObject hidingOverlay;
    [SerializeField] private TMP_Text hidingMessageText;
    [SerializeField] private TMP_Text countdownText;

    [Header("Objectives")]
    [SerializeField] private GameObject objectivesRoot;
    [SerializeField] private Image[] objectiveImages = new Image[5];

    private NetworkFirstPersonController controller;
    private PlayerGun gun;
    private Rigidbody body;

    private void Awake()
    {
        controller =
            GetComponent<NetworkFirstPersonController>();

        gun =
            GetComponent<PlayerGun>();

        body =
            GetComponent<Rigidbody>();

        if (hidingMessageText != null)
        {
            hidingMessageText.text =
                "Los props se están escondiendo, faltan";
        }
    }

    public void SetHiding(int seconds)
    {
        SetHunterControl(false);

        if (hidingOverlay != null)
        {
            hidingOverlay.SetActive(true);
        }

        if (objectivesRoot != null)
        {
            objectivesRoot.SetActive(false);
        }

        SetHideSeconds(seconds);
    }

    public void SetHideSeconds(int seconds)
    {
        if (countdownText == null)
            return;

        countdownText.text =
            $"{seconds} segundos para empezar.";
    }

    public void SetPlaying(int remainingObjectives)
    {
        SetHunterControl(true);

        if (hidingOverlay != null)
        {
            hidingOverlay.SetActive(false);
        }

        if (objectivesRoot != null)
        {
            objectivesRoot.SetActive(true);
        }

        SetObjectivesRemaining(
            remainingObjectives
        );
    }

    public void SetObjectivesRemaining(int remaining)
    {
        if (objectiveImages == null)
            return;

        remaining =
            Mathf.Clamp(
                remaining,
                0,
                objectiveImages.Length
            );

        for (int i = 0;
             i < objectiveImages.Length;
             i++)
        {
            if (objectiveImages[i] == null)
                continue;

            objectiveImages[i]
                .gameObject
                .SetActive(i < remaining);
        }
    }

    public void SetFinished()
    {
        SetHunterControl(false);

        if (hidingOverlay != null)
        {
            hidingOverlay.SetActive(false);
        }

        if (objectivesRoot != null)
        {
            objectivesRoot.SetActive(false);
        }
    }

    private void SetHunterControl(bool enabled)
    {
        if (controller != null)
        {
            controller.enabled = enabled;
        }

        if (gun != null)
        {
            gun.enabled = enabled;
        }

        if (body != null)
        {
            body.isKinematic = !enabled;
        }
    }
}