using TMPro;
using UnityEngine;

public sealed class PropRoundUI : MonoBehaviour
{
    [Header("Hiding")]
    [SerializeField] private GameObject hidingInfoRoot;
    [SerializeField] private TMP_Text hidingText;

    public void SetHiding(int seconds)
    {
        if (hidingInfoRoot != null)
        {
            hidingInfoRoot.SetActive(true);
        }

        SetHideSeconds(seconds);
    }

    public void SetHideSeconds(int seconds)
    {
        if (seconds <= 0)
        {
            Hide();
            return;
        }

        if (hidingText == null)
            return;

        hidingText.text =
            $"{seconds} segundos restantes para esconderse.\n" +
            "Cuando el Hunter despierte, buscá y activá los botones escondidos.";
    }

    public void Hide()
    {
        if (hidingInfoRoot != null)
        {
            hidingInfoRoot.SetActive(false);
        }
    }
}