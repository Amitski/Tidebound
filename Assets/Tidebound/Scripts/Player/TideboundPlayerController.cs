// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — TideboundPlayerController.cs
//
//  The character's body. The state float arrives via EmotionalStateManager and
//  reshapes everything: how fast the character can move, how high it jumps, how
//  quickly inputs translate into motion, how heavy gravity feels, whether a
//  double-jump is even available.
//
//  Same level. Same input bindings. The character moves through them
//  differently in hypomania and depression. That difference is the entire
//  point of this script.
//
//  Architecture notes:
//   • Reads EmotionalStateManager.Instance.CurrentValue every physics tick.
//     We poll rather than subscribe because we need the value continuously,
//     and polling a singleton getter is cheaper than UnityEvent dispatch.
//   • Input is read in Update (so we never miss a button press), physics is
//     applied in FixedUpdate (so the simulation is stable).
//   • Every "feel" parameter is an AnimationCurve evaluated against the state
//     float. The defaults below are starting points — tune in the Inspector.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.Events;
using Tidebound.Core;

namespace Tidebound.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class TideboundPlayerController : MonoBehaviour
    {
        // ═══ Movement: Horizontal Speed ═══════════════════════════════════════

        [Header("Horizontal Movement")]

        [Tooltip("Maximum horizontal speed at full hypomania. Multiplied by the state curve.")]
        [SerializeField] private float baseMaxSpeed = 7f;

        [Tooltip("Speed multiplier as a function of state.\n" +
                 "GDD: 100% in hypomania, 55–65% in depression.\n" +
                 "X axis = state float (0=dep, 1=hyp), Y axis = multiplier.")]
        [SerializeField] private AnimationCurve speedMultiplierByState =
            AnimationCurve.Linear(0f, 0.6f, 1f, 1.0f);

        [Tooltip("How long it takes velocity to reach the target. Lower = snappier.\n" +
                 "GDD: instant in hypomania, 2–4 frame delay in depression.\n" +
                 "Implemented as smoothing rather than literal frame delay — the\n" +
                 "felt experience of depression is heaviness, not buggy controls.")]
        [SerializeField] private AnimationCurve responsivenessByState =
            AnimationCurve.Linear(0f, 0.18f, 1f, 0.04f);

        [Tooltip("Multiplier on responsiveness when airborne. >1 makes air control sluggish.")]
        [SerializeField, Range(1f, 3f)] private float airSmoothingMultiplier = 1.4f;

        // ═══ Movement: Jumping ═══════════════════════════════════════════════

        [Header("Jumping")]

        [Tooltip("Initial vertical velocity at full hypomania. Multiplied by the state curve.")]
        [SerializeField] private float baseJumpForce = 14f;

        [Tooltip("Jump force multiplier by state.\n" +
                 "GDD: 100% in hypomania, 60% in depression.")]
        [SerializeField] private AnimationCurve jumpMultiplierByState =
            AnimationCurve.Linear(0f, 0.6f, 1f, 1.0f);

        [Tooltip("If the jump button is released while still rising, vertical velocity\n" +
                 "is multiplied by this value. Enables variable jump height —\n" +
                 "tap for short hop, hold for full jump. Standard platformer feel.")]
        [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.5f;

        // ═══ Double Jump (hypomania only) ═════════════════════════════════════

        [Header("Double Jump")]

        [Tooltip("Double-jump becomes available only above this state value.\n" +
                 "GDD: double-jump is a hypomania feature.")]
        [SerializeField, Range(0f, 1f)] private float doubleJumpStateThreshold = 0.7f;

        [Tooltip("Double-jump force as a fraction of the (state-modulated) primary jump.")]
        [SerializeField, Range(0.3f, 1f)] private float doubleJumpForceFactor = 0.85f;

        // ═══ Gravity ═════════════════════════════════════════════════════════

        [Header("Gravity")]

        [Tooltip("Gravity scale by state. >1 = heavier than baseline, <1 = lighter.\n" +
                 "Combines multiplicatively with global Physics2D gravity (set to -20 in\n" +
                 "Project Settings > Physics 2D).\n" +
                 "Default: 1.5 in depression (heavy), 0.85 in hypomania (floaty).")]
        [SerializeField] private AnimationCurve gravityMultiplierByState =
            AnimationCurve.Linear(0f, 1.5f, 1f, 0.85f);

        [Tooltip("Extra gravity multiplier applied while falling. Snappier landings,\n" +
                 "less floaty feel on descent. Standard platformer trick.")]
        [SerializeField, Range(1f, 3f)] private float fallGravityMultiplier = 1.6f;

        // ═══ Coyote Time & Jump Buffer ═══════════════════════════════════════

        [Header("Coyote Time & Jump Buffer")]

        [Tooltip("Grace window after walking off a ledge during which a jump still counts.\n" +
                 "Universal platformer kindness — but state-modulated below.")]
        [SerializeField, Range(0f, 0.3f)] private float maxCoyoteTime = 0.12f;

        [Tooltip("Coyote time multiplier by state. Hypomania = generous (world forgives you).\n" +
                 "Depression = stingy (the ledge is just gone).")]
        [SerializeField] private AnimationCurve coyoteMultiplierByState =
            AnimationCurve.Linear(0f, 0.25f, 1f, 1.0f);

        [Tooltip("Grace window before landing during which a queued jump still fires.")]
        [SerializeField, Range(0f, 0.3f)] private float maxJumpBuffer = 0.10f;

        // ═══ Ground Detection ════════════════════════════════════════════════

        [Header("Ground Detection")]

        [Tooltip("Empty Transform positioned at the character's feet. Create as a child object.")]
        [SerializeField] private Transform groundCheck;

        [Tooltip("Radius of the ground-check overlap circle.")]
        [SerializeField] private float groundCheckRadius = 0.15f;

        [Tooltip("Layers considered 'ground'. Create a 'Ground' layer and assign your\n" +
                 "ground/platform sprites to it, then check it here.")]
        [SerializeField] private LayerMask groundLayers;

        // ═══ Visuals ═════════════════════════════════════════════════════════

        [Header("Visuals")]

        [Tooltip("Optional. Used to flip the sprite when changing facing direction.")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        // ═══ Control ═════════════════════════════════════════════════════════

        [Header("Control")]

        [Tooltip("When false, the controller ignores all input. Used for cutscenes,\n" +
                 "Mask sequences, the post-Conversation Path, etc.")]
        [SerializeField] private bool inputEnabled = true;

        // ═══ Events ══════════════════════════════════════════════════════════

        [Header("Events")]

        public UnityEvent onJumped;
        public UnityEvent onDoubleJumped;
        public UnityEvent onLanded;

        // ═══ Internal state ══════════════════════════════════════════════════

        private Rigidbody2D rb;

        private float horizontalInput;
        private bool jumpPressed;
        private bool jumpHeld;
        private bool jumpReleased;

        private float horizontalVelocityRef;   // SmoothDamp's internal reference
        private bool isGrounded;
        private bool wasGrounded;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private bool hasDoubleJump;
        private bool facingRight = true;

        // ═══ Public read-only API ════════════════════════════════════════════

        public bool IsGrounded => isGrounded;
        public bool IsMoving   => rb != null && Mathf.Abs(rb.linearVelocity.x) > 0.1f;
        public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
        public bool FacingRight => facingRight;
        public bool InputEnabled
        {
            get => inputEnabled;
            set => inputEnabled = value;
        }

        // ═══ Unity lifecycle ═════════════════════════════════════════════════

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            if (groundCheck == null)
            {
                Transform found = transform.Find("GroundCheck");
                if (found != null) groundCheck = found;
                else Debug.LogWarning(
                    $"[{nameof(TideboundPlayerController)}] No GroundCheck assigned and " +
                    $"no child named 'GroundCheck' found. Create one and assign it.");
            }

            if (groundLayers.value == 0)
                Debug.LogWarning(
                    $"[{nameof(TideboundPlayerController)}] Ground Layers mask is empty. " +
                    $"The character will never register as grounded.");
        }

        private void Update()
        {
            ReadInput();
            CheckGrounded();
            UpdateTimers();
            HandleJumpRequest();
            UpdateFacing();
        }

        private void FixedUpdate()
        {
            ApplyHorizontalMovement();
            ApplyVariableGravity();
        }

        // ═══ Input ═══════════════════════════════════════════════════════════

        private void ReadInput()
        {
            if (!inputEnabled)
            {
                horizontalInput = 0f;
                jumpPressed = jumpHeld = jumpReleased = false;
                return;
            }

            // Legacy Input. We selected "Both" in Project Settings → Player so this
            // works alongside the new Input System. We'll migrate to the new system
            // when we wire up the Mask (input redirection) in Phase 1.
            horizontalInput = Input.GetAxisRaw("Horizontal");
            jumpPressed     = Input.GetButtonDown("Jump");
            jumpHeld        = Input.GetButton("Jump");
            jumpReleased    = Input.GetButtonUp("Jump");
        }

        // ═══ Ground Detection ════════════════════════════════════════════════

        private void CheckGrounded()
        {
            wasGrounded = isGrounded;

            isGrounded = groundCheck != null &&
                         Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayers);

            if (isGrounded && !wasGrounded)
            {
                onLanded?.Invoke();
                hasDoubleJump = true; // refresh on landing
            }
        }

        // ═══ Timers ══════════════════════════════════════════════════════════

        private void UpdateTimers()
        {
            float state = StateValue();

            // Coyote time: refreshed every frame while grounded; counts down once airborne.
            // Multiplier shrinks the window during depression.
            if (isGrounded)
                coyoteTimer = maxCoyoteTime * coyoteMultiplierByState.Evaluate(state);
            else
                coyoteTimer -= Time.deltaTime;

            // Jump buffer: starts at maxJumpBuffer the moment Jump is pressed, counts down.
            if (jumpPressed)
                jumpBufferTimer = maxJumpBuffer;
            else
                jumpBufferTimer -= Time.deltaTime;
        }

        // ═══ Jump Logic ══════════════════════════════════════════════════════

        private void HandleJumpRequest()
        {
            float state = StateValue();

            // Primary jump (with coyote time + jump buffer kindness).
            // Both timers must be positive — we just left the ground (or are still on it),
            // AND the player just pressed jump (or is about to land with a buffered press).
            if (jumpBufferTimer > 0f && coyoteTimer > 0f)
            {
                float force = baseJumpForce * jumpMultiplierByState.Evaluate(state);
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);

                // Consume both timers so we don't immediately re-trigger.
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;

                onJumped?.Invoke();
                return;
            }

            // Double jump — only above the hypomania threshold.
            // GDD: "double-jump" is one of the things the world LETS you do when high.
            if (jumpPressed
                && !isGrounded
                && hasDoubleJump
                && state >= doubleJumpStateThreshold)
            {
                float force = baseJumpForce
                              * jumpMultiplierByState.Evaluate(state)
                              * doubleJumpForceFactor;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
                hasDoubleJump = false;
                onDoubleJumped?.Invoke();
                return;
            }

            // Variable jump height: cut vertical velocity if jump was released early
            // while still rising. Tap = short hop, hold = full jump.
            if (jumpReleased && rb.linearVelocity.y > 0f)
            {
                rb.linearVelocity = new Vector2(
                    rb.linearVelocity.x,
                    rb.linearVelocity.y * jumpCutMultiplier);
            }
        }

        // ═══ Horizontal Physics ══════════════════════════════════════════════

        private void ApplyHorizontalMovement()
        {
            float state = StateValue();
            float maxSpeed   = baseMaxSpeed * speedMultiplierByState.Evaluate(state);
            float smoothTime = responsivenessByState.Evaluate(state);

            // Air control: harder to change direction in midair.
            if (!isGrounded) smoothTime *= airSmoothingMultiplier;

            float targetVelocityX = horizontalInput * maxSpeed;

            float newVelocityX = Mathf.SmoothDamp(
                rb.linearVelocity.x,
                targetVelocityX,
                ref horizontalVelocityRef,
                smoothTime,
                Mathf.Infinity,
                Time.fixedDeltaTime);

            rb.linearVelocity = new Vector2(newVelocityX, rb.linearVelocity.y);
        }

        // ═══ Gravity ═════════════════════════════════════════════════════════

        private void ApplyVariableGravity()
        {
            float state = StateValue();
            float multiplier = gravityMultiplierByState.Evaluate(state);

            // Heavier on the way down, regardless of state. Snappy landings.
            if (rb.linearVelocity.y < 0f) multiplier *= fallGravityMultiplier;

            rb.gravityScale = multiplier;
        }

        // ═══ Facing ══════════════════════════════════════════════════════════

        private void UpdateFacing()
        {
            if (Mathf.Abs(horizontalInput) < 0.01f) return;

            bool wantFacingRight = horizontalInput > 0f;
            if (wantFacingRight == facingRight) return;

            facingRight = wantFacingRight;
            if (spriteRenderer != null) spriteRenderer.flipX = !facingRight;
        }

        // ═══ State access ════════════════════════════════════════════════════

        private float StateValue()
        {
            // Defensive: if the manager isn't in the scene yet, default to mid-stable
            // so the character is at least playable for testing scenes that don't have
            // the manager wired up.
            return EmotionalStateManager.Instance != null
                ? EmotionalStateManager.Instance.CurrentValue
                : 0.5f;
        }

        // ═══ Editor: ground-check gizmo ══════════════════════════════════════

        private void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = isGrounded ? new Color(0.4f, 1f, 0.4f, 0.7f)
                                       : new Color(1f, 0.4f, 0.4f, 0.7f);
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
