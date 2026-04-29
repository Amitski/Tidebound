// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — EmotionalBackgroundColor.cs
//
//  The world's atmospheric color shifts with the state float. Hypomania bathes
//  the frame in warm pale gold — paper held up to morning sun. Depression
//  drains it to cool grey — paper in shadow, color quietly leaving. Stable
//  sits in the cream center.
//
//  This is not the painted environment art (that comes later, layered on top).
//  This is the canvas underneath everything. The "atmosphere" the world breathes.
//
//  Because we use Cinemachine, the actual Camera component on Main Camera is
//  what renders the background. We don't have to drive that directly — we just
//  set the Camera.backgroundColor on whichever camera is configured to clear
//  with a solid color.
//
//  Implementation note: a Gradient is the right tool here, not two-color lerp.
//  The journey through state isn't symmetric. Gold→cream is a different
//  emotional movement than cream→grey, and a gradient lets us shape the curve
//  of that movement (pause longer in the cream "stable" zone, drop faster
//  toward grey, etc.) with keyframes you can drag in the Inspector.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using Tidebound.Core;

namespace Tidebound.Rendering
{
    public class EmotionalBackgroundColor : MonoBehaviour
    {
        // ═══ Target ═══════════════════════════════════════════════════════════

        [Header("Target Camera")]

        [Tooltip("The Camera whose backgroundColor will be driven. Leave empty to\n" +
                 "auto-find Camera.main at startup. Camera must have Background Type\n" +
                 "set to 'Solid Color' in URP.")]
        [SerializeField] private Camera targetCamera;

        // ═══ Gradient ════════════════════════════════════════════════════════

        [Header("Gradient — Background by State")]

        [Tooltip("Color at each point on the state float.\n" +
                 "Time 0.0 = depression (cool grey).\n" +
                 "Time 0.5 = stable (warm cream).\n" +
                 "Time 1.0 = hypomania (warm pale gold).\n" +
                 "Tune by dragging keys in the Inspector — this is the single most\n" +
                 "expressive knob in the game.")]
        [SerializeField] private Gradient backgroundGradient;

        [Tooltip("Smoothing window for color changes. Catches manual state snaps\n" +
                 "from debug/scripted SetValue calls. Higher = more lag.")]
        [SerializeField, Range(0.05f, 3f)] private float colorSmoothTime = 0.4f;

        // ═══ Internals ═══════════════════════════════════════════════════════

        private Color currentColor;
        private Color colorVelocity;     // SmoothDamp reference vector for Color (4 channels)

        // ═══ Unity lifecycle ═════════════════════════════════════════════════

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;

            if (targetCamera == null)
            {
                Debug.LogWarning(
                    $"[{nameof(EmotionalBackgroundColor)}] No target camera assigned and " +
                    $"Camera.main returned null. Background color will not update.");
                enabled = false;
                return;
            }

            // Initialize the gradient with sensible defaults if none was set in Inspector.
            // Lets the script work out-of-the-box; designer can override anytime.
            if (backgroundGradient == null || backgroundGradient.colorKeys.Length == 0)
            {
                backgroundGradient = BuildDefaultGradient();
            }

            // Snap to the correct color at frame 1, no fade-in from black.
            currentColor = backgroundGradient.Evaluate(StateValue());
            targetCamera.backgroundColor = currentColor;
        }

        private void LateUpdate()
        {
            // LateUpdate so we read state AFTER EmotionalStateManager.Update has run,
            // and apply the color BEFORE the next frame renders.
            float state = StateValue();
            Color targetColor = backgroundGradient.Evaluate(state);

            // SmoothDamp on each color channel. Color implements arithmetic operators,
            // so we can SmoothDamp it via Vector4 conversion. Cheaper to do per-channel.
            currentColor = new Color(
                Mathf.SmoothDamp(currentColor.r, targetColor.r, ref colorVelocity.r, colorSmoothTime),
                Mathf.SmoothDamp(currentColor.g, targetColor.g, ref colorVelocity.g, colorSmoothTime),
                Mathf.SmoothDamp(currentColor.b, targetColor.b, ref colorVelocity.b, colorSmoothTime),
                Mathf.SmoothDamp(currentColor.a, targetColor.a, ref colorVelocity.a, colorSmoothTime));

            targetCamera.backgroundColor = currentColor;
        }

        // ═══ Default gradient ════════════════════════════════════════════════

        /// <summary>
        /// Builds a sensible default gradient if none was authored in the Inspector.
        /// Colors taken from the Tidebound Color System palette:
        ///   • Depression cool grey #9CA0A8
        ///   • Quiet Cream (stable) #F5F0E8
        ///   • Hypomania pale gold #F5E8C8
        /// Adjust the time values to skew where the cream "stable plateau" sits.
        /// </summary>
        private Gradient BuildDefaultGradient()
        {
            Gradient g = new Gradient();

            // GradientColorKey takes a color and a time (0..1).
            g.SetKeys(
                colorKeys: new[]
                {
                    new GradientColorKey(new Color32(0x9C, 0xA0, 0xA8, 0xFF), 0.00f), // depression grey
                    new GradientColorKey(new Color32(0xC8, 0xC4, 0xC0, 0xFF), 0.30f), // greying cream
                    new GradientColorKey(new Color32(0xF5, 0xF0, 0xE8, 0xFF), 0.55f), // quiet cream — stable plateau
                    new GradientColorKey(new Color32(0xF5, 0xE8, 0xC8, 0xFF), 1.00f), // hypomanic pale gold
                },
                alphaKeys: new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });

            return g;
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
