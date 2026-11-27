using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local input, presentation, and predicted ammo for the player's equipped firearm.
/// The Master Client remains responsible for validating shots and damage.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public sealed class PlayerWeaponController : MonoBehaviourPun
{
    [Header("Weapon")]
    [SerializeField] private GunDefinition startingGun;
    [SerializeField] private Transform weaponSocket;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private AudioSource audioSource;

    private GunCombatAuthority combatAuthority;
    private GameObject weaponVisual;
    private Animation weaponAnimation;
    private Transform muzzlePoint;

    private int magazineAmmo;
    private int reserveAmmo;
    private int requestSequence;
    private float nextLocalShotTime;
    private bool isReloading;

    public GunDefinition CurrentGun => startingGun;
    public int MagazineAmmo => magazineAmmo;
    public int ReserveAmmo => reserveAmmo;
    public float AimMovementMultiplier => startingGun != null ? startingGun.AimMovementMultiplier : 1f;

    private bool IsLocalPlayer => !PhotonNetwork.InRoom || photonView.IsMine;

    private void Awake()
    {
        combatAuthority = GetComponent<GunCombatAuthority>();
        if (aimCamera == null)
            aimCamera = GetComponentInChildren<Camera>(true);
    }

    private void Start()
    {
        if (startingGun == null)
        {
            Debug.LogError("PlayerWeaponController: falta asignar GunDefinition.", this);
            enabled = false;
            return;
        }

        magazineAmmo = startingGun.MagazineSize;
        reserveAmmo = startingGun.MaxReserveAmmo;

        if (!IsLocalPlayer)
            return;

        EquipVisual();
    }

    private void Update()
    {
        if (!IsLocalPlayer || startingGun == null)
            return;

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            bool shouldFire = startingGun.FireMode == GunFireMode.Automatic
                ? mouse.leftButton.isPressed
                : mouse.leftButton.wasPressedThisFrame;

            if (shouldFire)
                TryFire();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            TryReload();
    }

    private void EquipVisual()
    {
        if (startingGun.Prefab == null || weaponSocket == null)
            return;

        weaponVisual = Instantiate(startingGun.Prefab, weaponSocket);
        weaponVisual.transform.localPosition = startingGun.FirstPersonPosition;
        weaponVisual.transform.localRotation = Quaternion.identity;

        weaponAnimation = weaponVisual.GetComponentInChildren<Animation>(true);
        muzzlePoint = FindEquivalentChild(weaponVisual.transform, startingGun.MuzzlePoint);

        if (muzzlePoint == null)
            muzzlePoint = FindChildByName(weaponVisual.transform, "Muzzle");

        PlayAnimation(startingGun.Animations.Equip);
    }

    private void TryFire()
    {
        if (startingGun.FireMode == GunFireMode.Burst || isReloading || Time.time < nextLocalShotTime)
            return;

        if (magazineAmmo <= 0)
        {
            PlaySound(startingGun.Audio.Empty);
            return;
        }

        if (aimCamera == null)
        {
            Debug.LogError("PlayerWeaponController: no se encontró la cámara para apuntar.", this);
            return;
        }

        nextLocalShotTime = Time.time + startingGun.CadenceSeconds;
        magazineAmmo--;

        PlaySound(startingGun.Audio.Shot);
        PlayAnimation(startingGun.Animations.Fire);
        SpawnEffect(startingGun.Effects.Muzzle, muzzlePoint != null ? muzzlePoint.position : aimCamera.transform.position,
            muzzlePoint != null ? muzzlePoint.rotation : aimCamera.transform.rotation);

        Vector3 direction = aimCamera.transform.forward.normalized;
        int sequence = ++requestSequence;

        if (combatAuthority == null)
        {
            Debug.LogError("PlayerWeaponController: falta GunCombatAuthority en el jugador.", this);
            return;
        }

        combatAuthority.RequestShot(startingGun, sequence, direction);
    }

    private void TryReload()
    {
        if (isReloading || magazineAmmo >= startingGun.MagazineSize || reserveAmmo <= 0)
            return;

        if (combatAuthority == null)
        {
            Debug.LogError("PlayerWeaponController: falta GunCombatAuthority en el jugador.", this);
            return;
        }

        isReloading = true;
        PlaySound(startingGun.Audio.Reload);
        PlayAnimation(startingGun.Animations.Reload);
        combatAuthority.RequestReload(startingGun, ++requestSequence);
        StartCoroutine(FinishLocalReload(startingGun.ReloadTime));
    }

    private IEnumerator FinishLocalReload(float duration)
    {
        yield return new WaitForSeconds(duration);
        isReloading = false;
    }

    /// <summary>Called only with the ammo state confirmed by the Master Client.</summary>
    public void ApplyAuthoritativeAmmo(int sequence, int authoritativeMagazine, int authoritativeReserve)
    {
        magazineAmmo = Mathf.Clamp(authoritativeMagazine, 0, startingGun.MagazineSize);
        reserveAmmo = Mathf.Clamp(authoritativeReserve, 0, startingGun.MaxReserveAmmo);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null)
            return;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    private void PlayAnimation(AnimationClip clip)
    {
        if (clip == null || weaponAnimation == null)
            return;

        if (weaponAnimation.GetClip(clip.name) == null)
            weaponAnimation.AddClip(clip, clip.name);

        weaponAnimation.Play(clip.name);
    }

    private static void SpawnEffect(GameObject effect, Vector3 position, Quaternion rotation)
    {
        if (effect == null)
            return;

        GameObject instance = Instantiate(effect, position, rotation);
        Destroy(instance, 3f);
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }

    private static Transform FindEquivalentChild(Transform instanceRoot, Transform prefabPoint)
    {
        if (prefabPoint == null)
            return null;

        string path = prefabPoint.name;
        Transform parent = prefabPoint.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        if (path.StartsWith(instanceRoot.name + "/"))
            path = path.Substring(instanceRoot.name.Length + 1);

        return instanceRoot.Find(path);
    }
}
