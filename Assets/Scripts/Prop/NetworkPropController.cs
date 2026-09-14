using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public sealed class NetworkPropController : MonoBehaviourPun
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 5f;

    [Header("Camera")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float mouseSensitivity = 0.1f;

    [Header("Scaling")]
    [SerializeField] private float scaleSpeed = 0.5f;

    private const float MaxLookAngle = 70f;
    private const float GroundCheckDistance = 0.15f;

    private Rigidbody body;
    private Collider bodyCollider;
    private Camera playerCamera;
    private PropVisual propVisual;

    private Vector2 moveInput;

    private float cameraYaw;
    private float cameraPitch;

    private bool jumpRequested;
    private bool canScale;
    private bool rotationLocked;

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
        bodyCollider = GetComponent<Collider>();
        propVisual = GetComponent<PropVisual>();

        playerCamera =
            GetComponentInChildren<Camera>(true);
    }

    private void Start()
    {
        body.isKinematic = !IsLocalPlayer;
        body.useGravity = IsLocalPlayer;

        body.constraints =
            RigidbodyConstraints.FreezeRotation;

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

        if (!IsLocalPlayer)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Para poder probar el prefab en una escena sin
        // sistema de partida.
        if (!PhotonNetwork.InRoom)
        {
            canScale = true;
        }
    }

    private void Update()
    {
        if (!IsLocalPlayer)
            return;

        ReadMovementInput();
        ReadCameraInput();
        ReadJumpInput();
        ReadScaleInput();
        ReadRotationLock();
    }

    private void FixedUpdate()
    {
        if (!IsLocalPlayer)
            return;

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
    }

    private void ReadCameraInput()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null || cameraPivot == null)
            return;

        Vector2 mouseDelta =
            mouse.delta.ReadValue();

        cameraYaw +=
            mouseDelta.x * mouseSensitivity;

        cameraPitch -=
            mouseDelta.y * mouseSensitivity;

        cameraPitch = Mathf.Clamp(
            cameraPitch,
            -MaxLookAngle,
            MaxLookAngle
        );

        cameraPivot.localRotation =
            Quaternion.Euler(
                cameraPitch,
                cameraYaw,
                0f
            );

        // Si la rotaci�n no est� bloqueada,
        // el prop acompa�a horizontalmente a la c�mara.
        if (!rotationLocked && propVisual != null)
        {
            propVisual.SetVisualRotation(
                cameraYaw
            );
        }
    }

    private void ReadRotationLock()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.rKey.wasPressedThisFrame)
        {
            rotationLocked = !rotationLocked;
        }
    }

    private void ReadJumpInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpRequested = true;
        }
    }

    private void ReadScaleInput()
    {
        if (!canScale || propVisual == null)
            return;

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        // Mantener E para agrandar.
        if (keyboard.eKey.isPressed)
        {
            propVisual.ChangeScale(
                scaleSpeed * Time.deltaTime
            );
        }
        // Mantener Q para achicar.
        else if (keyboard.qKey.isPressed)
        {
            propVisual.ChangeScale(
                -scaleSpeed * Time.deltaTime
            );
        }
    }

    private void Move()
    {
        if (playerCamera == null)
            return;

        Vector3 forward =
            playerCamera.transform.forward;

        Vector3 right =
            playerCamera.transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 direction =
            forward * moveInput.y +
            right * moveInput.x;

        direction =
            Vector3.ClampMagnitude(
                direction,
                1f
            );

        Vector3 movement =
            direction *
            moveSpeed *
            Time.fixedDeltaTime;

        body.MovePosition(
            body.position + movement
        );
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
        if (bodyCollider == null)
            return false;

        if (body.linearVelocity.y > 0.1f)
            return false;

        float distance =
            bodyCollider.bounds.extents.y +
            GroundCheckDistance;

        return Physics.Raycast(
            bodyCollider.bounds.center,
            Vector3.down,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore
        );
    }

    // El futuro sistema de partida usa esto.
    // true durante escondite, false al terminar.
    public void SetCanScale(bool value)
    {
        canScale = value;
    }
}