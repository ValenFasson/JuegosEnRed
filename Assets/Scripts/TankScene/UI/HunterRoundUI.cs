using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HunterRoundUI :
    MonoBehaviour
{
    [Header("Hiding")]
    [SerializeField] private GameObject hidingPanel;
    [SerializeField] private TMP_Text hidingText;

    [Header("Objectives")]
    [SerializeField] private GameObject objectivesRoot;
    [SerializeField] private Image[] objectiveImages;

    private NetworkFirstPersonController controller;
    private PlayerGun gun;
    private Rigidbody body;

    private void Awake()
    {
        controller =
            GetComponent<
                NetworkFirstPersonController
            >();

        gun =
            GetComponent<PlayerGun>();

        body =
            GetComponent<Rigidbody>();
    }

    public void SetHiding(
        int seconds)
    {
        if (controller != null)
        {
            controller.enabled = false;
        }

        if (gun != null)
        {
            gun.enabled = false;
        }

        if (body != null)
        {
            body.isKinematic = true;
        }

        if (hidingPanel != null)
        {
            hidingPanel.SetActive(true);
        }

        if (objectivesRoot != null)
        {
            objectivesRoot.SetActive(false);
        }

        SetHideSeconds(seconds);
    }

    public void SetHideSeconds(
        int seconds)
    {
        if (hidingText == null)
            return;

        hidingText.text =
            "Los props se están escondiendo\n" +
            $"Faltan {seconds} segundos para empezar";
    }

    public void SetPlaying(
        int remainingObjectives)
    {
        if (body != null)
        {
            body.isKinematic = false;
        }

        if (controller != null)
        {
            controller.enabled = true;
        }

        if (gun != null)
        {
            gun.enabled = true;
        }

        if (hidingPanel != null)
        {
            hidingPanel.SetActive(false);
        }

        if (objectivesRoot != null)
        {
            objectivesRoot.SetActive(true);
        }

        SetObjectivesRemaining(
            remainingObjectives
        );
    }

    public void SetObjectivesRemaining(
        int remaining)
    {
        if (objectiveImages == null)
            return;

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
        if (controller != null)
        {
            controller.enabled = false;
        }

        if (gun != null)
        {
            gun.enabled = false;
        }

        if (body != null)
        {
            body.isKinematic = true;
        }

        if (hidingPanel != null)
        {
            hidingPanel.SetActive(false);
        }

        if (objectivesRoot != null)
        {
            objectivesRoot.SetActive(false);
        }
    }
}