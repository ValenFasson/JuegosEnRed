using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public sealed class TankController : MonoBehaviourPun
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 120f;

    [Header("Shooting")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float fireCooldown = 0.4f;
    [SerializeField] private float projectileSpawnDistance = 1.2f;

    private Rigidbody body;

    private float moveInput;
    private float turnInput;
    private float nextFireTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (photonView.IsMine)
        {
            body.isKinematic = false;
            body.useGravity = true;

            body.constraints =
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
        }
        else
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return;

        ReadInput();

        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame &&
            Time.time >= nextFireTime)
        {
            Shoot();
        }
    }

    private void FixedUpdate()
    {
        if (!photonView.IsMine)
            return;

        Move();
        Rotate();
    }

    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            moveInput = 0f;
            turnInput = 0f;
            return;
        }

        moveInput = 0f;
        turnInput = 0f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            moveInput += 1f;

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            moveInput -= 1f;

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            turnInput += 1f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            turnInput -= 1f;
    }

    private void Move()
    {
        Vector3 movement =
            transform.forward *
            moveInput *
            moveSpeed *
            Time.fixedDeltaTime;

        body.MovePosition(body.position + movement);
    }

    private void Rotate()
    {
        Quaternion rotation = Quaternion.Euler(
            0f,
            turnInput * turnSpeed * Time.fixedDeltaTime,
            0f
        );

        body.MoveRotation(body.rotation * rotation);
    }

    private void Shoot()
    {
        nextFireTime = Time.time + fireCooldown;

        Vector3 spawnPosition =
            transform.position +
            transform.forward * projectileSpawnDistance +
            Vector3.up * 0.5f;

        PhotonNetwork.Instantiate(
            projectilePrefab.name,
            spawnPosition,
            transform.rotation
        );
    }
}