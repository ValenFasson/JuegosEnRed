using UnityEngine;

public class PhotonCanvasPanels : MonoBehaviour
{
    [SerializeField] private GameObject roomsPanel;
    [SerializeField] private PhotonConnectionTestPanel connectionTests;

    private bool testsWereVisible;

    private void Start()
    {
        if (roomsPanel == null || connectionTests == null || transform.IsChildOf(roomsPanel.transform) ||
            connectionTests.transform.IsChildOf(roomsPanel.transform) || connectionTests.ViewRoot == null ||
            connectionTests.ViewRoot.transform.IsChildOf(roomsPanel.transform) ||
            roomsPanel.transform.IsChildOf(connectionTests.ViewRoot.transform) ||
            transform.IsChildOf(connectionTests.ViewRoot.transform))
        {
            Debug.LogError("Canvas: asigná Rooms Panel y Connection Tests; colocá el controlador fuera de los paneles.", this);
            enabled = false;
            return;
        }
        ShowRooms();
    }

    private void Update()
    {
        // F8 and the test panel's Close button use the same navigation state.
        if (testsWereVisible != connectionTests.Visible)
            RefreshPanels();
    }

    public void ShowRooms()
    {
        if (connectionTests != null)
            connectionTests.ClosePanel();
        RefreshPanels();
    }

    public void ShowConnectionTests()
    {
        if (connectionTests != null)
            connectionTests.OpenPanel();
        RefreshPanels();
    }

    public void ToggleConnectionTests()
    {
        if (connectionTests != null && connectionTests.Visible)
            ShowRooms();
        else
            ShowConnectionTests();
    }

    public void CloseConnectionTests() => ShowRooms();

    private void RefreshPanels()
    {
        testsWereVisible = connectionTests != null && connectionTests.Visible;
        if (roomsPanel != null && roomsPanel.activeSelf == testsWereVisible)
            roomsPanel.SetActive(!testsWereVisible);
    }
}
