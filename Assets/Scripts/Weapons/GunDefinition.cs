using UnityEngine;

/// <summary>
/// Inspector-configurable, shared weapon definition. Do not store per-player ammo here.
/// </summary>
[CreateAssetMenu(fileName = "GunDefinition", menuName = "Weapons/Gun Definition")]
public sealed class GunDefinition : ScriptableObject, IGun
{
    [Header("Identity")]
    [SerializeField] private string gunName;
    [SerializeField] private GunType type;
    [SerializeField] private Sprite icon;

    [Header("Firing")]
    [SerializeField] private GunFireMode fireMode = GunFireMode.SemiAutomatic;
    [SerializeField, Min(0.01f)] private float cadenceSeconds = 0.25f;
    [SerializeField, Min(0f)] private float range = 60f;
    [SerializeField, Min(1)] private int projectilesPerShot = 1;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float baseDamage = 25f;
    [SerializeField, Min(1f)] private float headshotMultiplier = 2f;

    [Header("Ammo")]
    [SerializeField, Min(1)] private int magazineSize = 12;
    [SerializeField, Min(0)] private int maxReserveAmmo = 24;
    [SerializeField, Min(0f)] private float reloadTime = 1.5f;

    [Header("Handling")]
    [SerializeField, Min(0f)] private float equipTime = 0.25f;
    [SerializeField, Tooltip("X = yaw and Y = pitch kick per shot.")] private Vector2 recoil;
    [SerializeField, Range(0f, 1f)] private float aimMovementMultiplier = 0.8f;

    [Header("Presentation")]
    [SerializeField] private GameObject prefab;
    [SerializeField] private Vector3 firstPersonPosition;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private GunAnimationSet animations;
    [SerializeField] private GunAudioSet audio;
    [SerializeField] private GunEffectSet effects;

    public string GunName => gunName;
    public GunType Type => type;
    public Sprite Icon => icon;

    public GunFireMode FireMode => fireMode;
    public float CadenceSeconds => cadenceSeconds;
    public float Range => range;
    public int ProjectilesPerShot => projectilesPerShot;

    public float BaseDamage => baseDamage;
    public float HeadshotMultiplier => headshotMultiplier;

    public int MagazineSize => magazineSize;
    public int MaxReserveAmmo => maxReserveAmmo;
    public float ReloadTime => reloadTime;

    public float EquipTime => equipTime;
    public Vector2 Recoil => recoil;
    public float AimMovementMultiplier => aimMovementMultiplier;

    public GameObject Prefab => prefab;
    public Vector3 FirstPersonPosition => firstPersonPosition;
    public Transform MuzzlePoint => muzzlePoint;
    public GunAnimationSet Animations => animations;
    public GunAudioSet Audio => audio;
    public GunEffectSet Effects => effects;
}
