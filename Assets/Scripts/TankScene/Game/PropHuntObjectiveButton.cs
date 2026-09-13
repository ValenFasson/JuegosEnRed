using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class PropHuntObjectiveButton : MonoBehaviour
{
    [SerializeField, Range(0, 1)] private int buttonId;
    private static readonly List<PropHuntObjectiveButton> Buttons = new List<PropHuntObjectiveButton>();
    private Renderer buttonRenderer;
    private MaterialPropertyBlock appearance;
    private bool? displayedActivation;
    public int ButtonId => buttonId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Buttons.Clear();

    private void Awake()
    {
        buttonRenderer = GetComponent<Renderer>();
        appearance = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        Buttons.Add(this);
        displayedActivation = null;
    }

    private void OnDisable()
    {
        Buttons.Remove(this);
        if (buttonRenderer != null)
            buttonRenderer.SetPropertyBlock(null);
    }

    public static PropHuntObjectiveButton Find(int id)
    {
        foreach (PropHuntObjectiveButton button in Buttons)
            if (button != null && button.buttonId == id)
                return button;
        return null;
    }

    public static PropHuntObjectiveButton FindNearby(Vector3 position)
    {
        PropHuntObjectiveButton nearest = null;
        float distance = (float)(PropHuntRoundRules.ButtonActivationRange * PropHuntRoundRules.ButtonActivationRange);
        foreach (PropHuntObjectiveButton button in Buttons)
        {
            if (button == null || TankGame.IsButtonActivated(button.buttonId))
                continue;
            float candidate = (position - button.transform.position).sqrMagnitude;
            if (candidate <= distance)
            {
                distance = candidate;
                nearest = button;
            }
        }
        return nearest;
    }

    private void Update()
    {
        bool activated = TankGame.IsButtonActivated(buttonId);
        if (displayedActivation == activated)
            return;
        displayedActivation = activated;
        Color color = activated ? new Color(0.15f, 1f, 0.25f) : new Color(1f, 0.65f, 0.05f);
        buttonRenderer.GetPropertyBlock(appearance);
        appearance.SetColor("_BaseColor", color);
        appearance.SetColor("_Color", color);
        buttonRenderer.SetPropertyBlock(appearance);
    }
}
