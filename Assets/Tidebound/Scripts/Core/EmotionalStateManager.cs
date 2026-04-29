// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — EmotionalStateManager.cs
//  
//  The single source of truth for the game's emotional state float.
//  
//  Exactly one of these exists at runtime. Every other system — player controller,
//  camera, palette, music, clearing, shadow figures, trail — subscribes to its
//  events or reads CurrentValue. Nothing else owns this number.
//  
//  For Phase 0, this runs a cosine lerp between 1.0 (hypomania) and 0.0
//  (depression) over a configurable period. Cosine is deliberate: the curve is
//  slowest at the extremes and fastest through the middle. That matches how
//  transitions actually feel from the inside — you don't notice the early drift,
//  and by the time you do, you're already halfway down. The roadmap calls this
//  "the slow fade." This is it.
//  
//  In later phases, autoRun will be gated by stage logic. Manual SetValue() is
//  how debug tools and scripted stages override it.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.Events;

namespace Tidebound.Core
{
    // Run before everything else so downstream systems in Update() see fresh values this frame,
    // not last frame. DefaultExecutionOrder of -1000 means this runs WAY before the default (0).
    [DefaultExecutionOrder(-1000)]
    public class EmotionalStateManager : MonoBehaviour
    {
        // Simple singleton. We only ever want one of these. Access from any other script
        // via EmotionalStateManager.Instance.CurrentValue, etc.
        public static EmotionalStateManager Instance { get; private set; }

        // ═══ Inspector: Cycle configuration ═══════════════════════════════════

        [Header("Cycle — Autorun Mode")]

        [Tooltip("Full cycle period. Half of this = time from hypomania to depression.\n" +
                 "GDD Phase 0 target: a single slow fade over 3–5 minutes → set this to 6–10.")]
        [Range(1f, 30f)]
        [SerializeField] private float cycleDurationMinutes = 8f;

        [Tooltip("Value the game starts at when this component wakes.\n" +
                 "GDD: player wakes in hypomania. Default 1.0.")]
        [Range(0f, 1f)]
        [SerializeField] private float startingValue = 1f;

        [Tooltip("If true, state auto-cycles on Update. If false, state only changes via SetValue().")]
        [SerializeField] private bool autoRun = true;

        [Header("Mixed State Override")]

        [Tooltip("Externally flagged during Stage 5. Oscillation behavior is Phase 2 work —\n" +
                 "for now this only tags the Phase. Visual/mechanical mixed-state effects come later.")]
        [SerializeField] private bool isMixed = false;

        [Header("Debug")]

        [Tooltip("Log every phase transition to the Console. Turn off before ship.")]
        [SerializeField] private bool logPhaseTransitions = true;

        // ═══ Inspector: Events ════════════════════════════════════════════════

        [Header("Events — subscribe for state changes")]

        [Tooltip("Fires every frame the state value changes. Argument: new value 0..1.\n" +
                 "Per-frame events are acceptable because this is the ONE state float;\n" +
                 "downstream systems need per-frame updates to animate smoothly.")]
        public UnityEvent<float> onStateChanged;

        [Tooltip("Fires only on Phase transitions (e.g. Building → Depression).\n" +
                 "Use this for one-shot reactions: change the music track, trigger a cue, etc.")]
        public UnityEvent<EmotionalState.Phase> onPhaseChanged;

        // ═══ Public read-only state ═══════════════════════════════════════════

        // [field: SerializeField] makes the backing field of this auto-property visible
        // in the Inspector, so we can watch it tick live during Play without Debug mode.
        // The setter stays private — external code cannot mutate it directly.
        [field: SerializeField, Range(0f, 1f)]
        public float CurrentValue { get; private set; }

        public float PreviousValue { get; private set; }
        public bool IsRising  => CurrentValue > PreviousValue;
        public bool IsFalling => CurrentValue < PreviousValue;
        public bool IsMixed   => isMixed;

        public EmotionalState.Phase CurrentPhase => EmotionalState.GetPhase(CurrentValue, isMixed);

        // Convenience alias so external code can write EmotionalStateManager.Instance.Phase
        // instead of CurrentPhase. Both work; this just reads cleaner at call sites.
        public EmotionalState.Phase Phase => CurrentPhase;

        // ═══ Private state ════════════════════════════════════════════════════

        private EmotionalState.Phase lastPhase;
        private float elapsedCycleTime;

        // ═══ Unity lifecycle ══════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[{nameof(EmotionalStateManager)}] Duplicate instance on '{name}'. " +
                    $"Destroying this copy — the original on '{Instance.name}' remains authoritative.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CurrentValue  = startingValue;
            PreviousValue = startingValue;

            // Offset elapsed time so the cosine curve actually starts AT startingValue,
            // not at 1.0. Without this, setting startingValue=0.5 would visibly snap to 1.0
            // on frame 1 before the cosine kicks in. Acos gives us the phase offset we need.
            float clampedForAcos = Mathf.Clamp(2f * startingValue - 1f, -1f, 1f);
            float initialAngle = Mathf.Acos(clampedForAcos);
            elapsedCycleTime = (initialAngle / (2f * Mathf.PI)) * (cycleDurationMinutes * 60f);

            lastPhase = CurrentPhase;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            PreviousValue = CurrentValue;

            if (autoRun)
            {
                elapsedCycleTime += Time.deltaTime;
                CurrentValue = ComputeCosineValue(elapsedCycleTime);
            }
            // In manual mode, CurrentValue only moves when SetValue() is called from elsewhere.

            // Fire the per-frame event unconditionally. Downstream systems that only
            // care about meaningful changes can filter themselves, but most (camera
            // zoom, palette tint, trail width) need continuous updates to look smooth.
            onStateChanged?.Invoke(CurrentValue);

            // Phase transitions fire only on actual change.
            EmotionalState.Phase nowPhase = CurrentPhase;
            if (nowPhase != lastPhase)
            {
                onPhaseChanged?.Invoke(nowPhase);
                if (logPhaseTransitions)
                {
                    Debug.Log($"[StateManager] {lastPhase} → {nowPhase}  (value={CurrentValue:F3})");
                }
                lastPhase = nowPhase;
            }
        }

        // ═══ The curve itself ═════════════════════════════════════════════════

        /// <summary>
        /// Cosine mapping: elapsed=0 → 1.0 (hypomania), elapsed=half-period → 0.0 (depression),
        /// elapsed=full-period → back to 1.0. Curve is slowest at extremes (so a tester sitting
        /// in Stage 1 for 90 seconds barely notices it drifting) and fastest through the middle
        /// (so once they notice, they've already crossed). This shape is the phenomenological
        /// core of the slow fade.
        /// </summary>
        private float ComputeCosineValue(float elapsedSeconds)
        {
            float periodSeconds = cycleDurationMinutes * 60f;
            if (periodSeconds <= Mathf.Epsilon) return startingValue;

            float angle = (elapsedSeconds / periodSeconds) * 2f * Mathf.PI;
            // cos(0) = 1, cos(π) = -1. Remap [-1, 1] → [0, 1].
            return (Mathf.Cos(angle) + 1f) * 0.5f;
        }

        // ═══ Public control API ═══════════════════════════════════════════════

        /// <summary>
        /// Manually set the state value. Automatically disables autoRun — call Resume() to
        /// hand control back to the cosine cycle. Used by debug HUD, scripted stage logic,
        /// and specific sequences (e.g. The Conversation, The Path).
        /// </summary>
        public void SetValue(float newValue)
        {
            autoRun = false;
            CurrentValue = Mathf.Clamp01(newValue);
        }

        /// <summary>Toggle the Mixed modality (Stage 5).</summary>
        public void SetMixed(bool mixed) => isMixed = mixed;

        /// <summary>Freeze state at its current value. Does not affect isMixed.</summary>
        public void Pause() => autoRun = false;

        /// <summary>
        /// Resume auto-cycling. Cycle clock continues from where it was paused — we do NOT
        /// re-anchor to the current value, because that would create a visible snap.
        /// </summary>
        public void Resume() => autoRun = true;

        /// <summary>
        /// Reset the cycle clock and jump to a new starting value. Call between stages
        /// when we want a fresh slow-fade starting from a specific mood.
        /// </summary>
        public void ResetCycle(float newStartingValue = 1f)
        {
            startingValue = Mathf.Clamp01(newStartingValue);
            CurrentValue  = startingValue;
            PreviousValue = startingValue;

            float clampedForAcos = Mathf.Clamp(2f * startingValue - 1f, -1f, 1f);
            float initialAngle = Mathf.Acos(clampedForAcos);
            elapsedCycleTime = (initialAngle / (2f * Mathf.PI)) * (cycleDurationMinutes * 60f);

            lastPhase = CurrentPhase;
        }
    }
}
