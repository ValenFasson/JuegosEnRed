using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PhotonConnectionTestPanel : MonoBehaviour
{
    public static bool Available =>
        Application.isEditor ||
        Debug.isDebugBuild;

    public GameObject ViewRoot => viewRoot;

    [Header("View")]
    [SerializeField] private GameObject viewRoot;

    [SerializeField] private Text profileText;
    [SerializeField] private Text stateText;
    [SerializeField] private Text metricsText;
    [SerializeField] private Text cutText;
    [SerializeField] private Text transportText;

    [Header("Simulation")]
    [SerializeField] private Text lagText;
    [SerializeField] private Text jitterText;
    [SerializeField] private Text lossText;

    [SerializeField] private Slider lagSlider;
    [SerializeField] private Slider jitterSlider;
    [SerializeField] private Slider lossSlider;

    [Header("Profiles")]
    [SerializeField] private Button lag100Button;
    [SerializeField] private Button lag250Button;
    [SerializeField] private Button unstableButton;
    [SerializeField] private Button customButton;
    [SerializeField] private Button lossProfileButton;

    [Header("Disconnect Test")]
    [SerializeField] private Button cut30Button;
    [SerializeField] private Button cut120Button;

    private PhotonPeer peer;
    private PhotonConnectionTestSimulation simulation;

    private bool visible;
    private bool originalTrafficStats;

    private int lag;
    private int jitter;
    private int loss;

    private double restoreAt;
    private double nextPresentation;
    private double nextMetrics;

    private string profile =
        "Sin simulación aplicada";

    private string metrics;

    private long lostBaselineIn;
    private long lostBaselineOut;

    public bool Visible
    {
        get => visible;

        set
        {
            visible =
                value &&
                Available &&
                isActiveAndEnabled;

            if (viewRoot != null &&
                viewRoot.activeSelf != visible)
            {
                viewRoot.SetActive(visible);
            }

            nextMetrics = 0d;
            nextPresentation = 0d;
        }
    }

    private void OnEnable()
    {
        if (!Available)
        {
            enabled = false;
            return;
        }

        if (viewRoot == null ||
            transform.IsChildOf(viewRoot.transform))
        {
            Debug.LogError(
                "Asigná View Root y colocá este script fuera del panel.",
                this
            );

            enabled = false;
            return;
        }

        Visible = false;

        peer =
            PhotonNetwork.NetworkingClient
                .LoadBalancingPeer;

        simulation =
            new PhotonConnectionTestSimulation(
                peer
            );

        originalTrafficStats =
            peer.TrafficStatsEnabled;

        peer.TrafficStatsEnabled = true;

        ConfigureSlider(
            lagSlider,
            500
        );

        ConfigureSlider(
            jitterSlider,
            500
        );

        ConfigureSlider(
            lossSlider,
            100
        );
    }

    private void OnDisable()
    {
        simulation?.Dispose();
        simulation = null;

        if (peer != null)
        {
            peer.TrafficStatsEnabled =
                originalTrafficStats;
        }

        restoreAt = 0d;

        Visible = false;

        profile =
            "Sin simulación aplicada";

        lag = 0;
        jitter = 0;
        loss = 0;

        metrics = null;
        nextMetrics = 0d;

        lostBaselineIn = 0;
        lostBaselineOut = 0;
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.f8Key
                .wasPressedThisFrame)
        {
            Visible = !Visible;
        }

        if (restoreAt > 0d &&
            Time.realtimeSinceStartupAsDouble >=
            restoreAt)
        {
            RestoreConnection();
        }

        simulation?.Maintain();

        if (!Visible)
            return;

        if (Time.realtimeSinceStartupAsDouble >=
            nextPresentation)
        {
            nextPresentation =
                Time.realtimeSinceStartupAsDouble +
                0.15d;

            RefreshPresentation();
        }

        if (Time.realtimeSinceStartupAsDouble <
            nextMetrics)
        {
            return;
        }

        nextMetrics =
            Time.realtimeSinceStartupAsDouble +
            0.5d;

        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        if (peer == null)
            return;

        TrafficStatsGameLevel stats =
            peer.TrafficStatsGameLevel;

        double seconds =
            Math.Max(
                1d,
                peer.TrafficStatsElapsedMs /
                1000d
            );

        metrics =
            $"RTT/Ping: {peer.RoundTripTime} ms\n" +
            $"Variación RTT: {peer.RoundTripTimeVariance}\n" +
            $"Reenvíos fiables: {peer.ResentReliableCommands}\n" +
            $"Mensajes entrada: {stats.TotalIncomingMessageCount}\n" +
            $"Mensajes salida: {stats.TotalOutgoingMessageCount}\n" +
            $"Promedio entrada: {stats.TotalIncomingMessageCount / seconds:F1}/s\n" +
            $"Promedio salida: {stats.TotalOutgoingMessageCount / seconds:F1}/s\n" +
            $"Paquetes simulados perdidos entrada: " +
            $"{Math.Max(0L, peer.NetworkSimulationSettings.LostPackagesIn - lostBaselineIn)}\n" +
            $"Paquetes simulados perdidos salida: " +
            $"{Math.Max(0L, peer.NetworkSimulationSettings.LostPackagesOut - lostBaselineOut)}";
    }

    private void RefreshPresentation()
    {
        if (peer == null)
            return;

        bool cut =
            restoreAt > 0d;

        bool udp =
            peer.TransportProtocol ==
            ConnectionProtocol.Udp;

        SetText(
            profileText,
            profile
        );

        SetText(
            stateText,
            PhotonLocalValidation.DescribeState()
        );

        SetText(
            metricsText,
            metrics ?? "Midiendo Photon..."
        );

        SetText(
            cutText,
            cut
                ? $"Corte activo: " +
                  $"{Math.Max(0d, restoreAt - Time.realtimeSinceStartupAsDouble):F0} s."
                : ""
        );

        SetText(
            transportText,
            udp
                ? ""
                : "La simulación de pérdida/corte está pensada para UDP."
        );

        SetText(
            lagText,
            $"Lag: {lag} ms"
        );

        SetText(
            jitterText,
            $"Jitter: ±{jitter} ms"
        );

        SetText(
            lossText,
            $"Pérdida: {loss}%"
        );

        lagSlider?.SetValueWithoutNotify(lag);
        jitterSlider?.SetValueWithoutNotify(jitter);
        lossSlider?.SetValueWithoutNotify(loss);

        SetInteractable(
            lagSlider,
            !cut
        );

        SetInteractable(
            jitterSlider,
            !cut
        );

        SetInteractable(
            lossSlider,
            !cut && udp
        );

        SetInteractable(
            lag100Button,
            !cut
        );

        SetInteractable(
            lag250Button,
            !cut
        );

        SetInteractable(
            unstableButton,
            !cut
        );

        SetInteractable(
            customButton,
            !cut
        );

        SetInteractable(
            lossProfileButton,
            !cut && udp
        );

        SetInteractable(
            cut30Button,
            !cut &&
            udp &&
            PhotonNetwork.InRoom
        );

        SetInteractable(
            cut120Button,
            !cut &&
            udp &&
            PhotonNetwork.InRoom
        );
    }

    public void SetLag(float value)
    {
        if (restoreAt > 0d)
            return;

        lag =
            Mathf.Clamp(
                Mathf.RoundToInt(value),
                0,
                500
            );

        jitter =
            Math.Min(
                jitter,
                lag
            );
    }

    public void SetJitter(float value)
    {
        if (restoreAt > 0d)
            return;

        jitter =
            Mathf.Clamp(
                Mathf.RoundToInt(value),
                0,
                lag
            );
    }

    public void SetLoss(float value)
    {
        if (restoreAt > 0d)
            return;

        loss =
            Mathf.Clamp(
                Mathf.RoundToInt(value),
                0,
                100
            );
    }

    public void OpenPanel()
    {
        Visible = true;
    }

    public void ClosePanel()
    {
        Visible = false;
    }

    public void TogglePanel()
    {
        Visible = !Visible;
    }

    public void ApplyLag100()
    {
        TryApplyProfile(
            100,
            0,
            0,
            "Lag +100 ms"
        );
    }

    public void ApplyLag250()
    {
        TryApplyProfile(
            250,
            0,
            0,
            "Lag +250 ms"
        );
    }

    public void ApplyUnstable()
    {
        TryApplyProfile(
            200,
            100,
            0,
            "Conexión inestable"
        );
    }

    public void ApplyLoss()
    {
        TryApplyProfile(
            100,
            0,
            5,
            "5% pérdida"
        );
    }

    public void ApplyCustom()
    {
        TryApplyProfile(
            lag,
            jitter,
            loss,
            "Personalizado"
        );
    }

    public void Cut30()
    {
        TryStartCut(30d);
    }

    public void Cut120()
    {
        TryStartCut(120d);
    }

    private void TryApplyProfile(
        int milliseconds,
        int variation,
        int percentage,
        string name)
    {
        if (simulation == null ||
            restoreAt > 0d)
        {
            return;
        }

        ApplyProfile(
            milliseconds,
            variation,
            percentage,
            name
        );
    }

    private void ApplyProfile(
        int lagMilliseconds,
        int jitterMilliseconds,
        int lossPercentage,
        string name)
    {
        lag = lagMilliseconds;

        jitter =
            Math.Min(
                lagMilliseconds,
                jitterMilliseconds
            );

        loss = lossPercentage;

        simulation.Apply(
            lag,
            jitter,
            loss
        );

        profile =
            $"{name} — lag {lag} ms, " +
            $"jitter ±{jitter} ms, " +
            $"pérdida {loss}%.";

        Debug.Log(
            "[Photon test] " + profile
        );
    }

    private void TryStartCut(
        double seconds)
    {
        if (simulation == null ||
            restoreAt > 0d ||
            !PhotonNetwork.InRoom)
        {
            return;
        }

        ApplyProfile(
            0,
            0,
            100,
            $"Corte {seconds:F0}s"
        );

        restoreAt =
            Time.realtimeSinceStartupAsDouble +
            seconds;
    }

    public void RestoreConnection()
    {
        restoreAt = 0d;

        if (simulation != null)
        {
            ApplyProfile(
                0,
                0,
                0,
                "Simulación desactivada"
            );
        }
    }

    public void ResetStatistics()
    {
        if (peer == null)
            return;

        peer.TrafficStatsReset();
        peer.TrafficStatsEnabled = true;

        lostBaselineIn =
            peer.NetworkSimulationSettings
                .LostPackagesIn;

        lostBaselineOut =
            peer.NetworkSimulationSettings
                .LostPackagesOut;

        nextMetrics = 0d;
    }

    public void LogSnapshot()
    {
        Debug.Log(
            "[Photon test]\n" +
            PhotonLocalValidation.DescribeState()
        );
    }

    private static void ConfigureSlider(
        Slider slider,
        int maximum)
    {
        if (slider == null)
            return;

        slider.minValue = 0;
        slider.maxValue = maximum;
        slider.wholeNumbers = true;
    }

    private static void SetText(
        Text target,
        string value)
    {
        if (target != null &&
            target.text != value)
        {
            target.text = value;
        }
    }

    private static void SetInteractable(
        Selectable target,
        bool value)
    {
        if (target != null)
        {
            target.interactable = value;
        }
    }
}