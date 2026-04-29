// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — EmotionalState.cs
//  The interpretive layer over the master state float.
//  
//  There is one number in this game: a float from 0.0 (depression) to 1.0
//  (hypomania). That number drives movement, color, music, camera, everything.
//  This static class is the ONLY place where we decide what that number MEANS
//  — what count as "depression," what count as "stable," where the thresholds
//  live. Every other system asks this class, so every system reads the same
//  truth. If we ever need to tune the thresholds, we tune them here and the
//  whole game shifts at once.
//
//  This file is a plain static utility — no MonoBehaviour, no state, no
//  allocations. Safe to call from anywhere, every frame.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Tidebound.Core
{
    public static class EmotionalState
    {
        /// <summary>
        /// The named phases the game recognizes. Note that "Mixed" is not a point
        /// on the 0..1 axis — it is a distinct modality. The state float still has
        /// a value during mixed state, but the world behaves fundamentally differently,
        /// so it's flagged externally by EmotionalStateManager.SetMixed().
        /// </summary>
        public enum Phase
        {
            DeepDepression,   // < 0.15  — Stage 4 ink void zone
            Depression,       // < 0.30  — Stages 2 and 4
            Building,         // between stable and an extreme, transitional in either direction
            Stable,           // the narrow real-home. Per GDD: "alive but grounded," not flat
            Hypomania,        // > 0.70  — Stages 1 and 3
            Mixed             // externally flagged. Stage 5.
        }

        // ─── Thresholds ────────────────────────────────────────────────────────
        // These match the GDD's specific "state < 0.3" / "state > 0.5" callouts.
        // If the game ever feels wrong ("depression triggers too early" / "hypomania
        // comes on too strong") — this is where we tune. Do not sprinkle magic
        // numbers in other scripts. Reference these constants instead.

        public const float DEEP_DEPRESSION_CEILING = 0.15f;
        public const float DEPRESSION_CEILING      = 0.30f;
        public const float STABLE_FLOOR            = 0.40f;
        public const float STABLE_CEILING          = 0.60f;
        public const float HYPOMANIA_FLOOR         = 0.70f;

        // ─── Phase resolution ──────────────────────────────────────────────────

        /// <summary>
        /// Given the current float value (and the external mixed flag), returns the
        /// named Phase. This is the primary entry point for systems that want to
        /// branch on "what phase are we in?" rather than do math on the raw float.
        /// </summary>
        public static Phase GetPhase(float stateValue, bool isMixed = false)
        {
            if (isMixed) return Phase.Mixed;

            if (stateValue <= DEEP_DEPRESSION_CEILING) return Phase.DeepDepression;
            if (stateValue <= DEPRESSION_CEILING)      return Phase.Depression;
            if (stateValue <  STABLE_FLOOR)            return Phase.Building;
            if (stateValue <= STABLE_CEILING)          return Phase.Stable;
            if (stateValue <  HYPOMANIA_FLOOR)         return Phase.Building;
            return Phase.Hypomania;
        }

        // ─── Convenience predicates ────────────────────────────────────────────
        // These make other code read like English.
        // Prefer these over raw comparisons: `EmotionalState.IsDepressive(v)` over `v < 0.4f`.

        public static bool IsHypomanic(float stateValue)       => stateValue >  STABLE_CEILING;
        public static bool IsDepressive(float stateValue)      => stateValue <  STABLE_FLOOR;
        public static bool IsStable(float stateValue)          => stateValue >= STABLE_FLOOR
                                                                && stateValue <= STABLE_CEILING;
        public static bool IsDeepDepression(float stateValue)  => stateValue <= DEEP_DEPRESSION_CEILING;

        /// <summary>
        /// Remaps the 0..1 state float into -1..+1, where -1 is full depression and
        /// +1 is full hypomania, with 0 at stable-center. Useful for bipolar scaling
        /// of things like gravity multipliers or camera offsets where the sign matters.
        /// </summary>
        public static float AsBipolar(float stateValue) => (stateValue * 2f) - 1f;
    }
}
