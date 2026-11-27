using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Receives fire/reload requests on the attacker's PhotonView. The Master Client
/// validates the request, performs the hitscan, and is the only caller that applies damage.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public sealed class GunCombatAuthority : MonoBehaviourPunCallbacks
{
    [SerializeField] private GunDefinition[] gunCatalog;
    [SerializeField, Min(0f)] private float eyeHeight = 1.5f;
    [SerializeField] private LayerMask hitMask = ~0;

    private sealed class WeaponState
    {
        public int Magazine;
        public int Reserve;
        public double NextShotAt;
        public double ReloadCompletesAt;
        public bool IsReloading;
        public GunDefinition Definition;
    }

    private readonly Dictionary<string, WeaponState> states = new Dictionary<string, WeaponState>();
    private PlayerWeaponController weaponController;

    private bool IsAuthority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    private void Awake()
    {
        weaponController = GetComponent<PlayerWeaponController>();
    }

    private void Start()
    {
        if (weaponController != null && weaponController.CurrentGun != null)
            GetState(weaponController.CurrentGun);
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            return;

        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.time;
        foreach (KeyValuePair<string, WeaponState> entry in states)
        {
            WeaponState state = entry.Value;
            if (!state.IsReloading || now < state.ReloadCompletesAt)
                continue;

            CompleteReload(state, entry.Key, 0);
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (newMasterClient == PhotonNetwork.LocalPlayer &&
            weaponController != null && weaponController.CurrentGun != null)
        {
            GunDefinition gun = weaponController.CurrentGun;
            WeaponState state = GetState(gun);
            RestoreState(gun.GunName, state);
        }
    }

    public void RequestShot(GunDefinition gun, int sequence, Vector3 direction)
    {
        if (gun == null || !IsLocalOwner())
            return;

        if (!PhotonNetwork.InRoom)
        {
            ProcessShot(gun, sequence, direction, 0);
            return;
        }

        photonView.RPC(nameof(RpcRequestShot), RpcTarget.MasterClient,
            gun.GunName, sequence, direction);
    }

    public void RequestReload(GunDefinition gun, int sequence)
    {
        if (gun == null || !IsLocalOwner())
            return;

        if (!PhotonNetwork.InRoom)
        {
            BeginReload(gun, sequence, 0);
            return;
        }

        photonView.RPC(nameof(RpcRequestReload), RpcTarget.MasterClient,
            gun.GunName, sequence);
    }

    [PunRPC]
    private void RpcRequestShot(string gunName, int sequence, Vector3 direction, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || !IsRequestFromOwner(info.Sender))
            return;

        GunDefinition gun = FindGun(gunName);
        if (gun == null)
            return;

        ProcessShot(gun, sequence, direction, info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RpcRequestReload(string gunName, int sequence, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || !IsRequestFromOwner(info.Sender))
            return;

        GunDefinition gun = FindGun(gunName);
        if (gun == null)
            return;

        BeginReload(gun, sequence, info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RpcSetOwnerAmmo(int sequence, int magazine, int reserve, PhotonMessageInfo info)
    {
        if (PhotonNetwork.InRoom && (info.Sender == null || !info.Sender.IsMasterClient))
            return;

        if (!IsLocalOwner() || weaponController == null)
            return;

        weaponController.ApplyAuthoritativeAmmo(sequence, magazine, reserve);
    }

    private void ProcessShot(GunDefinition gun, int sequence, Vector3 direction, int shooterActor)
    {
        WeaponState state = GetState(gun);
        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.time;

        bool valid = gun.FireMode != GunFireMode.Burst &&
                     direction.sqrMagnitude > 0.9f &&
                     direction.sqrMagnitude < 1.1f &&
                     !state.IsReloading &&
                     now >= state.NextShotAt &&
                     state.Magazine > 0;

        if (!valid)
        {
            SendAmmoToOwner(sequence, state);
            return;
        }

        state.Magazine--;
        state.NextShotAt = now + gun.CadenceSeconds;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        ResolveHitscan(gun, origin, direction.normalized, shooterActor, sequence);
        SendAmmoToOwner(sequence, state);
    }

    private void ResolveHitscan(GunDefinition gun, Vector3 origin, Vector3 direction, int shooterActor, int sequence)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, gun.Range, hitMask,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                continue;

            PlayerHealth target = hit.collider.GetComponentInParent<PlayerHealth>();
            if (target != null)
            {
                bool headshot = target.IsHeadshot(hit.collider);
                float damage = gun.BaseDamage * (headshot ? gun.HeadshotMultiplier : 1f);
                target.ApplyDamageFromMaster(damage, shooterActor, sequence);
            }

            GameObject impact = gun.Effects.Impact;
            if (impact != null)
            {
                GameObject instance = Instantiate(impact, hit.point, Quaternion.LookRotation(hit.normal));
                Destroy(instance, 3f);
            }

            break;
        }
    }

    private void BeginReload(GunDefinition gun, int sequence, int shooterActor)
    {
        WeaponState state = GetState(gun);
        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.time;

        if (state.IsReloading || state.Magazine >= gun.MagazineSize || state.Reserve <= 0)
        {
            SendAmmoToOwner(sequence, state);
            return;
        }

        state.IsReloading = true;
        if (PhotonNetwork.InRoom)
        {
            state.ReloadCompletesAt = now + gun.ReloadTime;
            PersistState(gun.GunName, state);
            return;
        }

        state.ReloadCompletesAt = now + gun.ReloadTime;
        offlineReloadSequence[gun.GunName] = sequence;
    }

    private readonly Dictionary<string, int> offlineReloadSequence = new Dictionary<string, int>();

    private void CompleteReload(WeaponState state, string gunName, int sequence)
    {
        int transfer = Mathf.Min(state.Definition.MagazineSize - state.Magazine, state.Reserve);
        state.Magazine += transfer;
        state.Reserve -= transfer;
        state.IsReloading = false;
        state.ReloadCompletesAt = 0d;
        PersistState(gunName, state);

        if (!PhotonNetwork.InRoom && offlineReloadSequence.TryGetValue(gunName, out int storedSequence))
        {
            sequence = storedSequence;
            offlineReloadSequence.Remove(gunName);
        }

        SendAmmoToOwner(sequence, state);
    }

    private WeaponState GetState(GunDefinition gun)
    {
        if (!states.TryGetValue(gun.GunName, out WeaponState state))
        {
            state = new WeaponState
            {
                Definition = gun,
                Magazine = gun.MagazineSize,
                Reserve = gun.MaxReserveAmmo
            };

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                RestoreState(gun.GunName, state);

            states.Add(gun.GunName, state);
        }

        return state;
    }

    private GunDefinition FindGun(string gunName)
    {
        if (gunCatalog != null)
        {
            foreach (GunDefinition gun in gunCatalog)
            {
                if (gun != null && gun.GunName == gunName)
                    return gun;
            }
        }

        if (weaponController != null && weaponController.CurrentGun != null &&
            weaponController.CurrentGun.GunName == gunName)
            return weaponController.CurrentGun;

        return null;
    }

    private void SendAmmoToOwner(int sequence, WeaponState state)
    {
        if (PhotonNetwork.InRoom)
            PersistState(state.Definition.GunName, state);

        if (!PhotonNetwork.InRoom)
        {
            if (weaponController != null)
                weaponController.ApplyAuthoritativeAmmo(sequence, state.Magazine, state.Reserve);
            return;
        }

        Player owner = photonView.Owner;
        if (owner != null)
        {
            photonView.RPC(nameof(RpcSetOwnerAmmo), owner, sequence, state.Magazine, state.Reserve);
        }
    }

    private bool IsLocalOwner()
    {
        return !PhotonNetwork.InRoom || photonView.IsMine;
    }

    private bool IsRequestFromOwner(Player sender)
    {
        return sender != null && photonView.Owner != null &&
               sender.ActorNumber == photonView.Owner.ActorNumber;
    }

    private void PersistState(string gunName, WeaponState state)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
            return;

        string prefix = GetStatePropertyPrefix(gunName);
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { prefix + "mag", state.Magazine },
            { prefix + "reserve", state.Reserve },
            { prefix + "next", state.NextShotAt },
            { prefix + "reload", state.ReloadCompletesAt }
        });
    }

    private void RestoreState(string gunName, WeaponState state)
    {
        string prefix = GetStatePropertyPrefix(gunName);
        Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;

        if (properties.TryGetValue(prefix + "mag", out object magazine) && magazine is int mag)
            state.Magazine = Mathf.Clamp(mag, 0, state.Definition.MagazineSize);
        if (properties.TryGetValue(prefix + "reserve", out object reserve) && reserve is int reserveAmmo)
            state.Reserve = Mathf.Clamp(reserveAmmo, 0, state.Definition.MaxReserveAmmo);
        if (properties.TryGetValue(prefix + "next", out object nextShot) && nextShot is double nextAt)
            state.NextShotAt = nextAt;
        if (properties.TryGetValue(prefix + "reload", out object reload) && reload is double reloadAt)
        {
            state.ReloadCompletesAt = reloadAt;
            state.IsReloading = reloadAt > PhotonNetwork.Time;
            if (!state.IsReloading)
                state.ReloadCompletesAt = 0d;
        }
    }

    private string GetStatePropertyPrefix(string gunName)
    {
        int actorNumber = photonView.Owner != null ? photonView.Owner.ActorNumber : 0;
        string safeGunName = gunName.Replace(" ", "_");
        return "weapon_" + actorNumber + "_" + safeGunName + "_";
    }
}
