using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PropHuntResultUI : MonoBehaviour
{
    [Header("Hunter Wins")]
    [SerializeField] private GameObject hunterWinsPanel;
    [SerializeField] private Button hunterLobbyButton;
    [SerializeField] private Text hunterStatusText;

    [Header("Props Wins")]
    [SerializeField] private GameObject propsWinsPanel;
    [SerializeField] private Button propsLobbyButton;
    [SerializeField] private Text propsStatusText;

    private GameWinner displayedWinner;

    private void Awake()
    {
        if (hunterWinsPanel == null || propsWinsPanel == null || hunterLobbyButton == null || propsLobbyButton == null || hunterStatusText == null || propsStatusText == null)
        {
            Debug.LogError("PropHuntResultUI: asigná los dos paneles, botones y textos de estado.", this);
            enabled = false;
            return;
        }

        hunterWinsPanel.SetActive(false);
        propsWinsPanel.SetActive(false);
    }

    public void ShowResult(GamePhase phase, GameWinner winner)
    {
        if (!enabled)
            return;

        GameWinner nextWinner = phase == GamePhase.Finished ? winner: GameWinner.None;

        if (displayedWinner == nextWinner)
            return;

        displayedWinner = nextWinner;
        hunterWinsPanel.SetActive(nextWinner == GameWinner.Hunter);
        propsWinsPanel.SetActive(nextWinner == GameWinner.Props);

        if (nextWinner == GameWinner.None)
            return;

        SetStatus("Volvé al lobby para elegir una room e iniciar otra partida.");
        hunterLobbyButton.interactable = true;
        propsLobbyButton.interactable = true;
        UnlockCursor();

        Button selectedButton = nextWinner == GameWinner.Hunter? hunterLobbyButton: propsLobbyButton;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(selectedButton.gameObject);
    }

    private void LateUpdate()
    {
        if (displayedWinner != GameWinner.None)
            UnlockCursor();
    }

    public void ReturnToLobby()
    {
        if (displayedWinner == GameWinner.None)
            return;

        PropHuntRoomManager roomManager = PropHuntRoomManager.Instance;
        if (roomManager == null)
        {
            SetStatus("No se encontró PropHuntRoomManager para volver al lobby.");
            return;
        }

        if (!roomManager.ReturnToLobby())
        {
            SetStatus(roomManager.StatusMessage);
            return;
        }

        hunterLobbyButton.interactable = false;
        propsLobbyButton.interactable = false;
        SetStatus("Volviendo al lobby...");
    }

    private void SetStatus(string message)
    {
        hunterStatusText.text = message;
        propsStatusText.text = message;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}