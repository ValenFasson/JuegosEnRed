using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class NetworkFirstPersonController : MonoBehaviourPun
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float jumpForce = 5f;

    [Header("Look")]
    [SerializeField] private Transform lookPivot;
    [SerializeField] private float mouseSensitivity = 0.1f;

    private const float MaxLookAngle = 85f;
    private const float GroundCheckDistance = 0.15f;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private Camera playerCamera;
    private Canvas localCanvas;

    private Vector2 moveInput;

    private float pitch;
    private float yawInput;

    private bool sprinting;
    private bool jumpRequested;

    private bool IsLocalPlayer
    {
        get
        {
            return !PhotonNetwork.InRoom || photonView.IsMine;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        playerCamera = GetComponentInChildren<Camera>(true);
        localCanvas = GetComponentInChildren<Canvas>(true);
    }

    private void Start()
    {
        body.isKinematic = !IsLocalPlayer;
        body.useGravity = IsLocalPlayer;

        body.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        if (playerCamera != null)
        {
            playerCamera.enabled = IsLocalPlayer;

            AudioListener listener =
                playerCamera.GetComponent<AudioListener>();

            if (listener != null)
            {
                listener.enabled = IsLocalPlayer;
            }
        }

        if (localCanvas != null)
        {
            localCanvas.gameObject.SetActive(IsLocalPlayer);
        }

        if (!IsLocalPlayer)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (!IsLocalPlayer)
            return;

        ReadMovementInput();
        ReadMouse();
        ReadJump();
    }

    private void FixedUpdate()
    {
        if (!IsLocalPlayer)
            return;

        Rotate();
        Move();

        if (jumpRequested)
        {
            TryJump();
            jumpRequested = false;
        }
    }

    private void ReadMovementInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            moveInput = Vector2.zero;
            sprinting = false;
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.wKey.isPressed)
            vertical += 1f;

        if (keyboard.sKey.isPressed)
            vertical -= 1f;

        if (keyboard.dKey.isPressed)
            horizontal += 1f;

        if (keyboard.aKey.isPressed)
            horizontal -= 1f;

        moveInput = Vector2.ClampMagnitude(
            new Vector2(horizontal, vertical),
            1f
        );

        sprinting =
            keyboard.leftShiftKey.isPressed ||
            keyboard.rightShiftKey.isPressed;
    }

    private void ReadMouse()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null || lookPivot == null)
            return;

        Vector2 mouseDelta = mouse.delta.ReadValue();

        yawInput +=
            mouseDelta.x * mouseSensitivity;

        pitch -=
            mouseDelta.y * mouseSensitivity;

        pitch = Mathf.Clamp(
            pitch,
            -MaxLookAngle,
            MaxLookAngle
        );

        lookPivot.localRotation =
            Quaternion.Euler(
                pitch,
                0f,
                0f
            );
    }

    private void ReadJump()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpRequested = true;
        }
    }

    private void Move()
    {
        Vector3 direction =
            transform.forward * moveInput.y +
            transform.right * moveInput.x;

        float speed =
            sprinting
                ? sprintSpeed
                : moveSpeed;

        Vector3 movement =
            direction *
            speed *
            Time.fixedDeltaTime;

        body.MovePosition(
            body.position + movement
        );
    }

    private void Rotate()
    {
        if (Mathf.Approximately(yawInput, 0f))
            return;

        Quaternion rotation =
            Quaternion.Euler(
                0f,
                yawInput,
                0f
            );

        body.MoveRotation(
            body.rotation * rotation
        );

        yawInput = 0f;
    }

    private void TryJump()
    {
        if (!IsGrounded())
            return;

        body.AddForce(
            Vector3.up * jumpForce,
            ForceMode.Impulse
        );
    }

    private bool IsGrounded()
    {
        if (body.linearVelocity.y > 0.1f)
            return false;

        float distance =
            capsule.bounds.extents.y +
            GroundCheckDistance;

        return Physics.Raycast(
            capsule.bounds.center,
            Vector3.down,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore
        );
    }
}