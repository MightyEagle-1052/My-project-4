using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakeCatcher.Player
{
    /// <summary>
    /// First-person walking, sprinting, jumping and looking with the Input System.
    /// The CharacterController handles collisions; this component applies gravity.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonPlayerController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Input Actions asset containing Player/Move, Look, Jump and Sprint.")]
        [SerializeField] private InputActionAsset inputActions;

        [Tooltip("Child camera (or camera pivot). Only its local pitch is changed.")]
        [SerializeField] private Transform viewTransform;

        [Header("Movement")]
        [Tooltip("Walking speed in metres per second.")]
        [Min(0f)] [SerializeField] private float walkSpeed = 4f;

        [Tooltip("Speed while holding Sprint, in metres per second.")]
        [Min(0f)] [SerializeField] private float sprintSpeed = 6.5f;

        [Tooltip("Allow running while Sprint is held and movement is requested.")]
        [SerializeField] private bool sprintEnabled = true;

        [Header("Jumping")]
        [Tooltip("Allow grounded jumps and the small timing allowances below.")]
        [SerializeField] private bool jumpEnabled = true;

        [Tooltip("Approximate jump height in metres.")]
        [Min(0f)] [SerializeField] private float jumpHeight = 1.1f;

        [Tooltip("Seconds after leaving an edge during which a jump still works. Zero disables this allowance.")]
        [Min(0f)] [SerializeField] private float coyoteTime = 0.1f;

        [Tooltip("Remember a new Jump press this many seconds before landing. Zero disables buffering.")]
        [Min(0f)] [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Gravity")]
        [Tooltip("Downward acceleration in metres per second squared. Keep negative.")]
        [SerializeField] private float gravity = -20f;

        [Tooltip("Small downward speed that keeps the controller touching the ground.")]
        [SerializeField] private float groundedVelocity = -2f;

        [Tooltip("Maximum falling speed in metres per second, entered as a positive number.")]
        [Min(0.1f)] [SerializeField] private float terminalFallSpeed = 50f;

        [Header("Look")]
        [Tooltip("Degrees per mouse pixel. Mouse delta already measures movement this frame.")]
        [Min(0f)] [SerializeField] private float mouseSensitivity = 0.12f;

        [Tooltip("Degrees per second at full gamepad stick deflection.")]
        [Min(0f)] [SerializeField] private float stickLookSpeed = 150f;

        [Tooltip("Maximum up/down viewing angle, in degrees.")]
        [Range(1f, 89f)] [SerializeField] private float pitchLimit = 85f;

        [Tooltip("Reverse vertical mouse and gamepad look.")]
        [SerializeField] private bool invertY;

        private CharacterController characterController;
        private InputActionAsset runtimeInputActions;
        private InputActionMap playerActions;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private float verticalVelocity;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private float pitch;
        private float viewYaw;
        private float viewRoll;
        private bool initialized;
        private bool hasFocus;
        private bool discardNextLookInput;
        private bool wantsCursorLocked = true;
        private bool movementEnabled = true;

        public bool MovementEnabled => movementEnabled;

        /// <summary>True while running is enabled and a movement direction plus Sprint is held.</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>Ground contact from the latest controller move, excluding an upward jump.</summary>
        public bool IsGrounded => characterController != null && characterController.enabled &&
                                  characterController.isGrounded && verticalVelocity <= 0f;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            hasFocus = Application.isFocused;

            if (inputActions == null || viewTransform == null ||
                viewTransform == transform || !viewTransform.IsChildOf(transform))
            {
                Debug.LogError(
                    "FirstPersonPlayerController needs an Input Actions asset and a child camera/pivot assigned in the Inspector.",
                    this);
                enabled = false;
                return;
            }

            // Each player owns an independent action instance. Disabling this player
            // therefore cannot disable another component's shared input asset.
            runtimeInputActions = Instantiate(inputActions);
            playerActions = runtimeInputActions.FindActionMap("Player", false);
            moveAction = playerActions?.FindAction("Move", false);
            lookAction = playerActions?.FindAction("Look", false);
            jumpAction = playerActions?.FindAction("Jump", false);
            sprintAction = playerActions?.FindAction("Sprint", false);

            if (moveAction == null || lookAction == null || jumpAction == null || sprintAction == null)
            {
                Debug.LogError(
                    "FirstPersonPlayerController requires Player/Move, Player/Look, Player/Jump and Player/Sprint in its Input Actions asset.",
                    this);
                enabled = false;
                return;
            }

            initialized = true;
        }

        private void OnEnable()
        {
            if (!initialized)
                return;

            hasFocus = Application.isFocused;
            ClearTransientInputState();
            SynchronizeViewRotation();
            playerActions.Enable();
            TryLockCursor();
        }

        private void OnDisable()
        {
            playerActions?.Disable();
            verticalVelocity = 0f;
            ClearTransientInputState();
            ReleaseCursor();
        }

        private void OnDestroy()
        {
            if (runtimeInputActions != null)
                Destroy(runtimeInputActions);
        }

        private void Update()
        {
            if (!initialized || !movementEnabled || !characterController.enabled)
            {
                ClearTransientInputState();
                return;
            }

            // Releasing the pointer during a time-scale pause also supports future
            // menus without letting this controller immediately capture it again.
            if (Time.timeScale <= 0f)
            {
                ClearTransientInputState();
                ReleaseCursor();
                return;
            }

            // Losing focus or releasing the pointer stops player input, but the
            // player still falls and lands while the rest of the world continues.
            if (!hasFocus)
            {
                Move(false);
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                wantsCursorLocked = false;
                ReleaseCursor();
                Move(false);
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                bool resumePressed =
                    (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                    (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) ||
                    (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

                if (resumePressed)
                {
                    wantsCursorLocked = true;
                    TryLockCursor();
                }

                // Ignore input on the resume frame so pointer recentering never
                // causes an unexpected camera turn or jump. Gravity still runs.
                Move(false);
                return;
            }

            Look();
            Move(true);
        }

        private void Look()
        {
            // Locking/recentering the pointer, including after focus changes, can
            // produce a large delta. Discard the first controlled look sample.
            if (discardNextLookInput)
            {
                discardNextLookInput = false;
                return;
            }

            Vector2 lookInput = lookAction.ReadValue<Vector2>();
            bool isPointer = lookAction.activeControl != null && lookAction.activeControl.device is Pointer;
            float sensitivity = isPointer ? mouseSensitivity : stickLookSpeed * Time.deltaTime;

            transform.Rotate(Vector3.up, lookInput.x * sensitivity, Space.World);
            float verticalDirection = invertY ? 1f : -1f;
            pitch = Mathf.Clamp(pitch + lookInput.y * sensitivity * verticalDirection, -pitchLimit, pitchLimit);
            viewTransform.localRotation = Quaternion.Euler(pitch, viewYaw, viewRoll);
        }

        private void SynchronizeViewRotation()
        {
            // Read the current pose again when control resumes, since another
            // system may have positioned the camera while this one was disabled.
            Vector3 localAngles = viewTransform.localEulerAngles;
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, localAngles.x), -pitchLimit, pitchLimit);
            viewYaw = localAngles.y;
            viewRoll = localAngles.z;
        }

        private void Move(bool acceptInput)
        {
            if (!acceptInput || !jumpEnabled)
                ClearTransientInputState();

            Vector2 moveInput = acceptInput
                ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f)
                : Vector2.zero;
            IsSprinting = acceptInput && sprintEnabled && moveInput.sqrMagnitude > 0.0001f && sprintAction.IsPressed();
            float speed = IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 horizontalVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;

            bool grounded = IsGrounded;
            if (grounded && verticalVelocity < 0f)
                verticalVelocity = groundedVelocity;

            if (acceptInput && jumpEnabled)
            {
                float now = Time.time;

                // A just-started jump can leave isGrounded true briefly. Only
                // refill the edge allowance while grounded and not moving up.
                if (grounded)
                    lastGroundedTime = now;

                // A press, rather than a held button, queues one jump. The
                // explicit press/grounded cases also support zero timing windows.
                bool jumpPressed = jumpAction.WasPressedThisFrame();
                if (jumpPressed)
                    lastJumpPressedTime = now;

                bool hasJumpRequest = jumpPressed ||
                    (jumpBufferTime > 0f && now - lastJumpPressedTime <= jumpBufferTime);
                bool canJump = grounded ||
                    (coyoteTime > 0f && now - lastGroundedTime <= coyoteTime);

                if (hasJumpRequest && canJump && jumpHeight > 0f)
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    lastJumpPressedTime = float.NegativeInfinity;
                    lastGroundedTime = float.NegativeInfinity;
                }
            }

            verticalVelocity = Mathf.Max(verticalVelocity + gravity * Time.deltaTime, -terminalFallSpeed);
            Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
            CollisionFlags collisions = characterController.Move(velocity * Time.deltaTime);

            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;

            if ((collisions & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
                verticalVelocity = groundedVelocity;
        }

        private void ClearTransientInputState()
        {
            lastJumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;
            IsSprinting = false;
        }

        /// <summary>
        /// Fully suspends/resumes this controller, including gravity, for example
        /// while a vehicle owns the player position. Re-enabling leaves the cursor
        /// free until left click, Enter or gamepad Start resumes control. To pause
        /// the whole world, a separate pause system must change Time.timeScale.
        /// This does not disable the CharacterController or change Time.timeScale.
        /// </summary>
        public void SetMovementEnabled(bool value)
        {
            if (movementEnabled == value)
                return;

            movementEnabled = value;
            verticalVelocity = 0f;
            wantsCursorLocked = false;
            ClearTransientInputState();

            if (value && initialized)
                SynchronizeViewRotation();

            ReleaseCursor();
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;

            if (!focused)
            {
                ClearTransientInputState();
                ReleaseCursor();
            }
            else
                TryLockCursor();
        }

        private void TryLockCursor()
        {
            if (!initialized || !isActiveAndEnabled || !hasFocus || !movementEnabled ||
                !wantsCursorLocked || Time.timeScale <= 0f)
                return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            discardNextLookInput = true;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            coyoteTime = Mathf.Max(0f, coyoteTime);
            jumpBufferTime = Mathf.Max(0f, jumpBufferTime);
            gravity = Mathf.Min(-0.01f, gravity);
            groundedVelocity = Mathf.Min(-0.01f, groundedVelocity);
            terminalFallSpeed = Mathf.Max(0.1f, terminalFallSpeed);
            mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
            stickLookSpeed = Mathf.Max(0f, stickLookSpeed);
            pitchLimit = Mathf.Clamp(pitchLimit, 1f, 89f);
        }
    }
}
