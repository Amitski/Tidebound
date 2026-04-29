// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — TideboundCamera.cs
//
//  The camera's contribution to the felt experience.
//
//  Cinemachine handles WHERE the camera points (following the player). This
//  script handles HOW MUCH the camera shows. It drives the CinemachineCamera's
//  orthographic lens size from the state float, so the world visibly opens up
//  in hypomania and presses in during depression.
//
//  GDD framing target: 100% baseline at hypomania, ~130% zoom-in at depression.
//  In orthographic terms: a smaller ortho size = more magnification = less of
//  the world visible = pressing-in feel. Hypomania ortho ≈ 5.625, depression
//  ortho ≈ 4.33 (5.625 / 1.30).
//
//  The state float already moves slowly (cosine over 8 minutes), so this
//  script does very little extra smoothing — only enough to absorb manual
//  SetValue() snaps from debug tools or scripted stages.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using Unity.Cinemachine;
using Tidebound.Core;

namespace Tidebound.Cameras
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class TideboundCamera : MonoBehaviour
    {
        // ═══ Orthographic size ════════════════════════════════════════════════

        [Header("Orthographic Size by State")]

        [Tooltip("The orthographic size at full hypomania (state = 1.0).\n" +
                 "5.625 matches a 1080p window at 100 PPU. Don't change unless you\n" +
                 "also change the project's pixels-per-unit convention.")]
        [SerializeField] private float baseOrthographicSize = 5.625f;

        [Tooltip("Multiplier on base size, evaluated against state.\n" +
                 "GDD: 100% at hypomania, ~77% at depression (= 1/1.3 zoom-in).\n" +
                 "X axis = state (0=dep, 1=hyp), Y axis = multiplier on base size.")]
        [SerializeField] private AnimationCurve sizeMultiplierByState =
            AnimationCurve.Linear(0f, 0.77f, 1f, 1.0f);

        [Tooltip("Smoothing window for size changes. Catches manual state snaps\n" +
                 "from debug/scripted SetValue calls. Higher = more lag.")]
        [SerializeField, Range(0.05f, 3f)] private float sizeSmoothTime = 0.6f;

        // ═══ Dead zone & damping by state ════════════════════════════════════

        [Header("Position Composer — Damping by State")]

        [Tooltip("If true, drives the PositionComposer's damping from the state float.\n" +
                 "Hypomania = snappy follow (low damping). Depression = laggy follow\n" +
                 "(higher damping), so the world feels heavier — even the camera\n" +
                 "struggles to keep up.")]
        [SerializeField] private bool driveDamping = true;

        [Tooltip("Damping in X/Y at hypomania. Lower = snappier.")]
        [SerializeField] private Vector2 hypomanicDamping = new Vector2(0.3f, 0.3f);

        [Tooltip("Damping in X/Y at depression. Higher = laggier, heavier feel.")]
        [SerializeField] private Vector2 depressionDamping = new Vector2(1.2f, 1.2f);

        // ═══ Internals ═══════════════════════════════════════════════════════

        private CinemachineCamera cmCamera;
        private CinemachinePositionComposer positionComposer;

        private float currentSize;
        private float sizeVelocity;

        // ═══ Unity lifecycle ═════════════════════════════════════════════════

        private void Awake()
        {
            cmCamera = GetComponent<CinemachineCamera>();

            // PositionComposer is the standard 2D follow component in Cinemachine 3.x.
            // It's added automatically when you create a 2D Camera via the GameObject menu.
            // Optional — we'll still drive ortho size if it's missing, just no damping mod.
            positionComposer = GetComponent<CinemachinePositionComposer>();
            if (positionComposer == null && driveDamping)
            {
                Debug.LogWarning(
                    $"[{nameof(TideboundCamera)}] No CinemachinePositionComposer found. " +
                    $"Damping modulation disabled. (Did you create this via " +
                    $"GameObject → Cinemachine → 2D Camera?)");
                driveDamping = false;
            }

            // Initialize size to wherever state currently is, so we don't see a snap on frame 1.
            float initialState = StateValue();
            currentSize = baseOrthographicSize * sizeMultiplierByState.Evaluate(initialState);
            ApplyOrthographicSize(currentSize);
        }

        private void LateUpdate()
        {
            // LateUpdate so we read state AFTER EmotionalStateManager.Update has run this
            // frame, and apply the size BEFORE Cinemachine's brain renders.
            float state = StateValue();

            UpdateOrthographicSize(state);

            if (driveDamping) UpdateDamping(state);
        }

        // ═══ Size ════════════════════════════════════════════════════════════

        private void UpdateOrthographicSize(float state)
        {
            float targetSize = baseOrthographicSize * sizeMultiplierByState.Evaluate(state);

            currentSize = Mathf.SmoothDamp(
                currentSize,
                targetSize,
                ref sizeVelocity,
                sizeSmoothTime);

            ApplyOrthographicSize(currentSize);
        }

        private void ApplyOrthographicSize(float size)
        {
            // CinemachineCamera.Lens is a public field of type LensSettings (a struct).
            // We can mutate the struct field directly — no need to copy-modify-assign.
            cmCamera.Lens.OrthographicSize = size;
        }

        // ═══ Damping ═════════════════════════════════════════════════════════

        private void UpdateDamping(float state)
        {
            // Lerp directly between the two configured damping vectors.
            Vector2 damping = Vector2.Lerp(depressionDamping, hypomanicDamping, state);

            // PositionComposer.Damping is a Vector3 (X, Y, Z) where Z controls
            // the dolly-in/out distance for 3D — irrelevant in 2D, leave at 0.
            positionComposer.Damping = new Vector3(damping.x, damping.y, 0f);
        }

        // ═══ State access ════════════════════════════════════════════════════

        private float StateValue()
        {
            return EmotionalStateManager.Instance != null
                ? EmotionalStateManager.Instance.CurrentValue
                : 0.5f;
        }

        // ═══ Public API ══════════════════════════════════════════════════════

        /// <summary>Force-refresh the size based on current state — skips smoothing.</summary>
        public void SnapToCurrentState()
        {
            float state = StateValue();
            currentSize = baseOrthographicSize * sizeMultiplierByState.Evaluate(state);
            sizeVelocity = 0f;
            ApplyOrthographicSize(currentSize);
        }
    }
}
