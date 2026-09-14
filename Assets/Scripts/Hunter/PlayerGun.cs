using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerGun : MonoBehaviourPun
{
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float fireCooldown = 0.5f;

    private Camera playerCamera;
    private float nextFireTime;

    private bool IsLocalPlayer
    {
        get
        {
            return !PhotonNetwork.InRoom || photonView.IsMine;
        }
    }

    private void Awake()
    {
        playerCamera =
            GetComponentInChildren<Camera>(true);
    }

    private void Update()
    {
        if (!IsLocalPlayer)
            return;

        Mouse mouse = Mouse.current;

        if (mouse == null)
            return;

        if (!mouse.leftButton.wasPressedThisFrame)
            return;

        if (Time.time < nextFireTime)
            return;

        Shoot();
    }

    private void Shoot()
    {
        if (projectilePrefab == null)
        {
            Debug.LogError(
                "No hay un proyectil asignado.",
                this
            );

            return;
        }

        if (muzzle == null)
        {
            Debug.LogError(
                "No hay un Muzzle asignado.",
                this
            );

            return;
        }

        if (playerCamera == null)
        {
            Debug.LogError(
                "No se encontró la cámara del Hunter.",
                this
            );

            return;
        }

        nextFireTime =
            Time.time + fireCooldown;

        Quaternion projectileRotation =
            Quaternion.LookRotation(
                playerCamera.transform.forward
            );

        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.Instantiate(
                projectilePrefab.name,
                muzzle.position,
                projectileRotation
            );
        }
        else
        {
            Instantiate(
                projectilePrefab,
                muzzle.position,
                projectileRotation
            );
        }
    }
}