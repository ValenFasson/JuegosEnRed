using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Uses Photon's real peer; no replacement transport or gameplay authority.
[DisallowMultipleComponent]
public sealed class PhotonConnectionTestPanel : MonoBehaviour
{
    public static bool Available => Application.isEditor || Debug.isDebugBuild || PhotonLocalValidation.Active;
    public GameObject ViewRoot => viewRoot;
    [Header("Vista de Canvas")]
    [SerializeField] private GameObject viewRoot;
    [SerializeField] private Text profileText;
    [SerializeField] private Text stateText;
    [SerializeField] private Text metricsText;
    [SerializeField] private Text cutText;
    [SerializeField] private Text transportText;
    [SerializeField] private Text lagText;
    [SerializeField] private Text jitterText;
    [SerializeField] private Text lossText;
    [SerializeField] private Slider lagSlider;
    [SerializeField] private Slider jitterSlider;
    [SerializeField] private Slider lossSlider;
    [SerializeField] private Button lag100Button;
    [SerializeField] private Button lag250Button;
    [SerializeField] private Button unstableButton;
    [SerializeField] private Button customButton;
    [SerializeField] private Button lossProfileButton;
    [SerializeField] private Button cut30Button;
    [SerializeField] private Button cut120Button;

    private bool visible;
    private double nextPresentation;
    public bool Visible
    {
        get => visible;
        set
        {
            visible = value && Available && isActiveAndEnabled;
            if (viewRoot != null && viewRoot.activeSelf != visible)
                viewRoot.SetActive(visible);
            nextMetrics = nextPresentation = 0d;
        }
    }

    private PhotonPeer peer;
    private PhotonConnectionTestSimulation simulation;
    private bool originalTrafficStats;
    private int lag, jitter, loss;
    private double restoreAt;
    private string profile = "Sin simulación aplicada";
    private string metrics;
    private double nextMetrics;
    private long lostBaselineIn, lostBaselineOut;

    private void OnEnable()
    {
        if (!Available)
        {
            enabled = false;
            return;
        }
        if (viewRoot == null || transform.IsChildOf(viewRoot.transform))
        {
            Debug.LogError("Canvas: asigná View Root y colocá PhotonConnectionTestPanel fuera de ese panel, en un objeto siempre activo.", this);
            enabled = false;
            return;
        }
        Visible = false;
        peer = PhotonNetwork.NetworkingClient.LoadBalancingPeer;
        simulation = new PhotonConnectionTestSimulation(peer);
        originalTrafficStats = peer.TrafficStatsEnabled;
        peer.TrafficStatsEnabled = true;
        ConfigureSlider(lagSlider, 500);
        ConfigureSlider(jitterSlider, 500);
        ConfigureSlider(lossSlider, 100);
    }

    private void OnDisable()
    {
        simulation?.Dispose();
        simulation = null;
        if (peer != null)
            peer.TrafficStatsEnabled = originalTrafficStats;
        restoreAt = 0d;
        Visible = false;
        profile = "Sin simulación aplicada";
        lag = jitter = loss = 0;
        metrics = null;
        nextMetrics = 0d;
        lostBaselineIn = lostBaselineOut = 0L;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            Visible = !Visible;
        if (restoreAt > 0d && peer.TransportProtocol != ConnectionProtocol.Udp)
        {
            RestoreConnection();
            Announce("Corte cancelado: la pérdida de paquetes de Photon requiere UDP.");
        }
        else if (restoreAt > 0d && Time.realtimeSinceStartupAsDouble >= restoreAt)
            RestoreConnection();
        simulation?.Maintain();
        if (Visible && Time.realtimeSinceStartupAsDouble >= nextPresentation)
        {
            nextPresentation = Time.realtimeSinceStartupAsDouble + 0.15d;
            RefreshPresentation();
        }
        if (!Visible || Time.realtimeSinceStartupAsDouble < nextMetrics)
            return;
        nextMetrics = Time.realtimeSinceStartupAsDouble + 0.5d;
        TrafficStatsGameLevel stats = peer.TrafficStatsGameLevel;
        double seconds = Math.Max(1d, peer.TrafficStatsElapsedMs / 1000d);
        metrics = $"RTT/ping: {peer.RoundTripTime} ms | Variación RTT: {peer.RoundTripTimeVariance}\n" +
            $"Reenvíos fiables UDP: {peer.ResentReliableCommands}\n" +
            $"Mensajes acumulados: entrada {stats.TotalIncomingMessageCount}, salida {stats.TotalOutgoingMessageCount}\n" +
            $"Promedio desde reset: entrada {stats.TotalIncomingMessageCount / seconds:F1}/s, salida {stats.TotalOutgoingMessageCount / seconds:F1}/s\n" +
            $"Máximo entre envíos/dispatch: {stats.LongestDeltaBetweenSending}/{stats.LongestDeltaBetweenDispatching} ms\n" +
            $"Paquetes descartados por simulación desde reset: entrada {Math.Max(0L, peer.NetworkSimulationSettings.LostPackagesIn - lostBaselineIn)}, salida {Math.Max(0L, peer.NetworkSimulationSettings.LostPackagesOut - lostBaselineOut)}";
    }

    private void RefreshPresentation()
    {
        bool cut = restoreAt > 0d;
        bool udp = peer.TransportProtocol == ConnectionProtocol.Udp;
        SetText(profileText, profile);
        SetText(cutText, cut ? $"Corte activo: restauración en {Math.Max(0d, restoreAt - Time.realtimeSinceStartupAsDouble):F0} s." : "");
        PhotonRoomBrowser browser = PhotonRoomBrowser.Instance;
        SetText(stateText, $"Estado: {PhotonNetwork.NetworkClientState} | Transporte: {peer.TransportProtocol}\nRegión: {PhotonNetwork.CloudRegion}\n" +
            (browser != null ? $"{browser.StatusMessage}\nRecuperando: {browser.IsRecovering} | Intentos: {browser.ReconnectAttempts}\nÚltima desconexión: {browser.LastDisconnectCause?.ToString() ?? "ninguna"}" : ""));
        SetText(metricsText, (PhotonNetwork.IsConnectedAndReady ? "" : "El ping es la última medición; no indica conexión activa.\n") + (metrics ?? "Midiendo Photon..."));
        SetText(transportText, udp ? "" : "Los cortes y la pérdida requieren UDP. Lag y jitter siguen disponibles.");
        SetText(lagText, $"Lag por dirección (ms): {lag}");
        SetText(jitterText, $"Jitter ± (ms; limitado al lag): {jitter}");
        SetText(lossText, $"Pérdida por dirección (%): {loss}");
        if (lagSlider != null) lagSlider.SetValueWithoutNotify(lag);
        if (jitterSlider != null) jitterSlider.SetValueWithoutNotify(jitter);
        if (lossSlider != null) lossSlider.SetValueWithoutNotify(loss);
        SetInteractable(lagSlider, !cut);
        SetInteractable(jitterSlider, !cut);
        SetInteractable(lossSlider, !cut && udp);
        SetInteractable(lossProfileButton, !cut && udp);
        SetInteractable(lag100Button, !cut);
        SetInteractable(lag250Button, !cut);
        SetInteractable(unstableButton, !cut);
        SetInteractable(customButton, !cut);
        SetInteractable(cut30Button, !cut && udp && PhotonNetwork.InRoom);
        SetInteractable(cut120Button, !cut && udp && PhotonNetwork.InRoom);
    }

    public void SetLag(float value) { if (restoreAt == 0d) { lag = Mathf.Clamp(Mathf.RoundToInt(value), 0, 500); jitter = Math.Min(jitter, lag); nextPresentation = 0d; } }
    public void SetJitter(float value) { if (restoreAt == 0d) { jitter = Mathf.Clamp(Mathf.RoundToInt(value), 0, lag); nextPresentation = 0d; } }
    public void SetLoss(float value) { if (restoreAt == 0d) { loss = Mathf.Clamp(Mathf.RoundToInt(value), 0, 100); nextPresentation = 0d; } }
    public void OpenPanel() => Visible = true;
    public void TogglePanel() => Visible = !Visible;
    public void ClosePanel() => Visible = false;
    public void ApplyLag100() => TryApplyProfile(100, 0, 0, "Lag: +100 ms por dirección");
    public void ApplyLag250() => TryApplyProfile(250, 0, 0, "Lag alto: +250 ms por dirección");
    public void ApplyUnstable() => TryApplyProfile(200, 100, 0, "Conexión inestable");
    public void ApplyLoss()
    {
        if (peer != null && peer.TransportProtocol == ConnectionProtocol.Udp)
            TryApplyProfile(100, 0, 5, "Pérdida: 5 %, lag 100 ms");
    }
    public void ApplyCustom() => TryApplyProfile(lag, jitter,
        peer != null && peer.TransportProtocol == ConnectionProtocol.Udp ? loss : 0, "Ajustes personalizados");
    public void Cut30() => TryStartCut(30d);
    public void Cut120() => TryStartCut(120d);
    private void TryApplyProfile(int milliseconds, int variation, int percentage, string name)
    {
        if (simulation != null && restoreAt == 0d)
            ApplyProfile(milliseconds, variation, percentage, name);
    }
    private void TryStartCut(double seconds)
    {
        if (simulation != null && restoreAt == 0d && PhotonNetwork.InRoom &&
            peer.TransportProtocol == ConnectionProtocol.Udp)
            StartCut(seconds);
    }
    public void ResetStatistics()
    {
        if (peer == null)
            return;
        peer.TrafficStatsReset();
        peer.TrafficStatsEnabled = true;
        lostBaselineIn = peer.NetworkSimulationSettings.LostPackagesIn;
        lostBaselineOut = peer.NetworkSimulationSettings.LostPackagesOut;
        nextMetrics = 0d;
    }
    public void LogSnapshot() => Debug.Log("[Photon test snapshot] " + profile + "\n" + metrics + "\n" + PhotonLocalValidation.DescribeState());
    private static void SetText(Text target, string value)
    {
        if (target != null && target.text != value)
            target.text = value;
    }

    private static void SetInteractable(Selectable target, bool value)
    {
        if (target != null)
            target.interactable = value;
    }

    private static void ConfigureSlider(Slider slider, int maximum)
    {
        if (slider == null)
            return;
        slider.minValue = 0;
        slider.maxValue = maximum;
        slider.wholeNumbers = true;
    }

    private void ApplyProfile(int lagMilliseconds, int jitterMilliseconds, int lossPercentage, string name)
    {
        restoreAt = 0d;
        lag = lagMilliseconds;
        jitter = Math.Min(lagMilliseconds, jitterMilliseconds);
        loss = lossPercentage;
        simulation.Apply(lag, jitter, loss);
        profile = $"{name} — lag {lag} ms, jitter ±{jitter} ms, pérdida {loss} %.";
        Announce(profile);
    }

    private void StartCut(double seconds)
    {
        ApplyProfile(0, 0, 100, $"Corte de {seconds:F0} s");
        restoreAt = Time.realtimeSinceStartupAsDouble + seconds;
    }

    public void RestoreConnection()
    {
        if (simulation != null)
            ApplyProfile(0, 0, 0, "Comunicación restaurada; simulación desactivada");
    }

    private static void Announce(string message)
    {
        Debug.Log("[Photon test] " + message);
        PhotonRoomBrowser.Instance?.Notify(message);
    }
}
