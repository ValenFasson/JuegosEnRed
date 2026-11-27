using UnityEngine;

/// <summary>
/// Common read-only configuration contract for firearms.
/// Runtime state such as current ammo and reload status belongs to a weapon instance.
/// </summary>
public interface IGun
{
    string GunName { get; }
    GunType Type { get; }
    Sprite Icon { get; }

    GunFireMode FireMode { get; }
    /// <summary>Seconds between shots; a smaller value means a faster weapon.</summary>
    float CadenceSeconds { get; }
    float Range { get; }
    int ProjectilesPerShot { get; }

    float BaseDamage { get; }
    float HeadshotMultiplier { get; }

    int MagazineSize { get; }
    int MaxReserveAmmo { get; }
    float ReloadTime { get; }

    float EquipTime { get; }
    Vector2 Recoil { get; }
    float AimMovementMultiplier { get; }

    GameObject Prefab { get; }
    Vector3 FirstPersonPosition { get; }
    Transform MuzzlePoint { get; }
    GunAnimationSet Animations { get; }
    GunAudioSet Audio { get; }
    GunEffectSet Effects { get; }
}

public enum GunType
{
    Pistol,
    SubmachineGun,
    Rifle,
    Shotgun,
    SniperRifle,
    MachineGun
}

public enum GunFireMode
{
    SemiAutomatic,
    Automatic,
    Burst
}

[System.Serializable]
public struct GunAnimationSet
{
    public AnimationClip Idle;
    public AnimationClip Fire;
    public AnimationClip Reload;
    public AnimationClip Equip;
}

[System.Serializable]
public struct GunAudioSet
{
    public AudioClip Shot;
    public AudioClip Reload;
    public AudioClip Equip;
    public AudioClip Empty;
}

[System.Serializable]
public struct GunEffectSet
{
    public GameObject Muzzle;
    public GameObject Impact;
    public GameObject Tracer;
}
