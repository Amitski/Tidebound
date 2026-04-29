// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — PlayerBreathing.cs
//
//  The figure breathes.
//
//  This is the smallest visual system in the game. A subtle vertical-scale
//  pulse synchronized to a breath rhythm, where the rhythm changes with state.
//  Players will not consciously notice it. Their bodies will. The figure stops
//  feeling like a moving rectangle and starts feeling like someone alive.
//
//  Two parameters move with state:
//    • RATE    — breaths per minute. Faster in hypomania (12–14 BPM),
//                slower in depression (5–7 BPM). Real human resting breath
//                is 12–20 BPM; depression's slow heavy breath sits below.
//    • DEPTH   — peak amplitude of the scale pulse. Shallower in hypomania
//                (small fast breaths), deeper in depression (long heavy
//                breaths drawn from somewhere lower).
//
//  These move in OPPOSITE directions: faster + shallower vs slower + deeper.
//  That's the felt difference between an excited breath and a depressed one.
//
//  Implementation notes:
//    • Uses a sine wave on a phase accumulator. Continuous, never resets,
//      no risk of visible "breath skipping" when state changes.
//    • Scales the figure's transform around the BOTTOM of its collider, not
//      the center. Otherwise the figure appears to bob into the ground every
//      exhale. Achieved by adjusting position alongside scale.
//    • Suspended while airborne — characters don't breathe mid-jump in any
//      animation tradition you've seen, and the breath looks weird overlaid
//      on jump motion.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using Tidebound.Core;
using Tidebound.Player;

namespace Tidebound.Player
{
    public class PlayerBreathing : MonoBehaviour
    {
        // ═══ Target ═══════════════════════════════════════════════════════════

        [Header("Target")]

        [Tooltip("The Transform to apply the breathing scale to. If empty, uses this object.\n" +
                 "When we have the cloak rig, this should point at the body root bone.")]
        [SerializeField] private Transform breathingTarget;

        [Tooltip("Optional reference to the player controller. If assigned, breathing\n" +
                 "suspends while airborne or moving fast — breath looks wrong layered\n" +
                 "over jumps. Leave empty to breathe always (useful for testing).")]
        [SerializeField] private TideboundPlayerController playerController;

        // ═══ Rate ═════════════════════════════════════════════════════════════

        [Header("Breath Rate by State")]

        [Tooltip("Breaths per minute at full hypomania. Faster, shallower —\n" +
                 "excited breath. 12–14 BPM is alert/anxious resting range.")]
        [SerializeField, Range(4f, 30f)] private float bpmAtHypomania = 13f;

        [Tooltip("Breaths per minute at full depression. Slow, heavy —\n" +
                 "the breath has to be drawn up from somewhere far away.\n" +
                 "5–7 BPM is meditative-slow / depressed range.")]
        [SerializeField, Range(2f, 15f)] private float bpmAtDepression = 6f;

        // ═══ Depth ════════════════════════════════════════════════════════════

        [Header("Breath Depth by State")]

        [Tooltip("Vertical scale variation at full hypomania, as a fraction.\n" +
                 "0.015 = ±1.5%. Small fast breaths.")]
        [SerializeField, Range(0f, 0.10f)] private float depthAtHypomania = 0.015f;

        [Tooltip("Vertical scale variation at full depression, as a fraction.\n" +
                 "0.035 = ±3.5%. Bigger slower breaths.")]
        [SerializeField, Range(0f, 0.10f)] private float depthAtDepression = 0.035f;

        [Tooltip("Multiplier applied to horizontal scale relative to vertical.\n" +
                 "0 = no horizontal pulse (default — breath is mostly vertical).\n" +
                 "0.4 = subtle chest expansion. Useful for organic-figure look.")]
        [SerializeField, Range(0f, 1f)] private float horizontalDepthMultiplier = 0.25f;

        // ═══ Anchor ══════════════════════════════════════════════════════════

        [Header("Anchor")]

        [Tooltip("Compensate position so the figure scales around its FEET, not its center.\n" +
                 "Without this, every exhale appears to push the figure half-into the ground.\n" +
                 "Disable only if your figure should scale around its center.")]
        [SerializeField] private bool anchorAtFeet = true;

        [Tooltip("Approximate height of the figure, used for the feet-anchor calculation.\n" +
                 "Should match the visible figure size, not the GameObject's transform scale.")]
        [SerializeField] private float figureHeight = 1.6f;

        // ═══ Smoothing ═══════════════════════════════════════════════════════

        [Header("Smoothing")]

        [Tooltip("How responsively rate/depth react to state changes. Higher = more lag.\n" +
                 "Breath rhythm should NOT snap when state snaps — that destroys the\n" +
                 "felt continuity. Keep this around 0.6 for a 'breath catches up to mood'\n" +
                 "feeling rather than 'mood instantly retunes the breath.'")]
        [SerializeField, Range(0.05f, 3f)] private float parameterSmoothTime = 0.6f;

        // ═══ Internals ═══════════════════════════════════════════════════════

        // Phase accumulator. Increments at currentRate radians per second.
        // We never reset this — it just keeps rising — so phase is continuous
        // across state changes.
        private float breathPhase;

        private float currentBpm;
        private float bpmVelocity;

        private float currentDepth;
        private float depthVelocity;

        // Cached so we can restore on disable / when breathing is suspended.
        private Vector3 baselineLocalScale;
        private Vector3 baselineLocalPosition;

        // ═══ Unity lifecycle ═════════════════════════════════════════════════

        private void Awake()
        {
            if (breathingTarget == null) breathingTarget = transform;
            if (playerController == null) playerController = GetComponentInParent<TideboundPlayerController>();

            baselineLocalScale    = breathingTarget.localScale;
            baselineLocalPosition = breathingTarget.localPosition;

            // Initialize live values to current state's targets, no first-frame snap.
            float state = StateValue();
            currentBpm   = Mathf.Lerp(bpmAtDepression, bpmAtHypomania, state);
            currentDepth = Mathf.Lerp(depthAtDepression, depthAtHypomania, state);
        }

        private void OnDisable()
        {
            // Restore baseline so the figure doesn't get stuck in a half-breath.
            if (breathingTarget != null)
            {
                breathingTarget.localScale    = baselineLocalScale;
                breathingTarget.localPosition = baselineLocalPosition;
            }
        }

        private void LateUpdate()
        {
            // LateUpdate so we apply scale AFTER physics has placed the figure this frame.
            // Otherwise the breath fights the player controller's velocity application.

            float state = StateValue();

            // Smooth-track BPM and depth.
            float targetBpm   = Mathf.Lerp(bpmAtDepression,   bpmAtHypomania,   state);
            float targetDepth = Mathf.Lerp(depthAtDepression, depthAtHypomania, state);

            currentBpm   = Mathf.SmoothDamp(currentBpm,   targetBpm,   ref bpmVelocity,   parameterSmoothTime);
            currentDepth = Mathf.SmoothDamp(currentDepth, targetDepth, ref depthVelocity, parameterSmoothTime);

            // Suspend breathing during airborne / fast horizontal motion.
            // Ease back to baseline rather than snapping.
            if (ShouldSuspendBreathing())
            {
                EaseToBaseline();
                return;
            }

            // Advance phase. BPM → cycles per second → radians per second.
            float cyclesPerSecond = currentBpm / 60f;
            breathPhase += cyclesPerSecond * 2f * Mathf.PI * Time.deltaTime;

            // Wrap phase so it doesn't grow unbounded over hours of play.
            // Floating point sine starts losing precision at ~10^7 radians.
            if (breathPhase > 1000f * Mathf.PI) breathPhase -= 1000f * Mathf.PI;

            // The breath itself: a sine wave on phase, scaled by depth.
            // Sin gives us a smooth in-out-in-out motion that feels right for breath.
            // Some animators prefer a slightly asymmetric curve (faster inhale, slower
            // exhale) — that's a Phase 1 nicety. Pure sine reads fine.
            float breathSignal = Mathf.Sin(breathPhase);

            float verticalAmount   = breathSignal * currentDepth;
            float horizontalAmount = breathSignal * currentDepth * horizontalDepthMultiplier;

            ApplyBreath(verticalAmount, horizontalAmount);
        }

        // ═══ Application ═════════════════════════════════════════════════════

        private void ApplyBreath(float verticalAmount, float horizontalAmount)
        {
            // Scale: y stretches/compresses, x optional subtle expansion.
            Vector3 scale = baselineLocalScale;
            scale.y *= 1f + verticalAmount;
            scale.x *= 1f + horizontalAmount;
            breathingTarget.localScale = scale;

            // Position compensation: when the figure scales taller, raise it by half the
            // scale gain so its feet stay planted. Otherwise it pulses up and down through
            // the ground.
            if (anchorAtFeet)
            {
                Vector3 pos = baselineLocalPosition;
                pos.y += (figureHeight * verticalAmount) * 0.5f;
                breathingTarget.localPosition = pos;
            }
        }

        private void EaseToBaseline()
        {
            // Simple exponential ease toward baseline. Used when breathing is suspended
            // (airborne, etc.) so the figure doesn't snap to default.
            const float easeSpeed = 8f;
            breathingTarget.localScale = Vector3.Lerp(
                breathingTarget.localScale, baselineLocalScale, Time.deltaTime * easeSpeed);
            breathingTarget.localPosition = Vector3.Lerp(
                breathingTarget.localPosition, baselineLocalPosition, Time.deltaTime * easeSpeed);
        }

        // ═══ Suspension logic ════════════════════════════════════════════════

        private bool ShouldSuspendBreathing()
        {
            if (playerController == null) return false;

            // Airborne: don't breathe. The breath looks wrong layered over jumps.
            if (!playerController.IsGrounded) return true;

            // Fast horizontal motion: also probably not the right time for a visible breath.
            // (Walking is fine — running, less so.) Threshold is half the base max speed.
            if (Mathf.Abs(playerController.Velocity.x) > 5f) return true;

            return false;
        }

        // ═══ State access ════════════════════════════════════════════════════

        private float StateValue()
        {
            return EmotionalStateManager.Instance != null
                ? EmotionalStateManager.Instance.CurrentValue
                : 0.5f;
        }
    }
}
