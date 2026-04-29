// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — PlayerTrail.cs
//
//  The line the figure leaves behind as it moves.
//
//  In hypomania: a confident gold trail, broad, persistent, expressive — the
//  visible record of where you've been and what you've done. The world keeps
//  it. The world *shows* it.
//
//  In depression: the same gesture, but thinner. Greyer. Fading faster than
//  you can produce it. You walk forward and the path behind you is already
//  dissolving. There is no record. Nothing holds.
//
//  This script is the first time the player's emotional state has a VISUAL
//  presence in the world that isn't just "the world tinted." The trail is
//  authored by the player — your motion creates it — but the state determines
//  whether that authorship survives.
//
//  Three things move with state:
//    • WIDTH      — confident vs hairline
//    • COLOR      — gold vs grey
//    • LIFETIME   — long-lived gesture vs immediate dissolve
//
//  Implementation: standard Unity TrailRenderer driven from script. We override
//  its widthCurve, colorGradient, and time properties every frame from the
//  state float. The TrailRenderer handles emission, vertex spawning, and
//  fade-out internally, which is exactly what we want — let Unity do the
//  heavy lifting, we just paint the gradient.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using Tidebound.Core;

namespace Tidebound.Player
{
    [RequireComponent(typeof(TrailRenderer))]
    public class PlayerTrail : MonoBehaviour
    {
        // ═══ Width ════════════════════════════════════════════════════════════

        [Header("Width by State")]

        [Tooltip("Maximum trail width at full hypomania. The gesture is bold —\n" +
                 "the figure marks the world with confidence.")]
        [SerializeField] private float widthAtHypomania = 0.18f;

        [Tooltip("Minimum trail width at full depression. The gesture barely\n" +
                 "registers — the line is thinner than the figure's outline.")]
        [SerializeField] private float widthAtDepression = 0.04f;

        [Tooltip("How width tapers along the trail's length.\n" +
                 "Default: starts at full width at the figure, tapers to ~30% at the tail.\n" +
                 "Don't set the tail to 0 — that creates an aliased pinch-point that\n" +
                 "looks worse than a soft taper.")]
        [SerializeField] private AnimationCurve widthTaperCurve =
            new AnimationCurve(
                new Keyframe(0f, 1.0f),
                new Keyframe(1f, 0.3f));

        // ═══ Color ════════════════════════════════════════════════════════════

        [Header("Color by State")]

        [Tooltip("Trail color at full hypomania (state = 1.0). Per the color system:\n" +
                 "warm gold #D4943A. Saturated, confident, alive.")]
        [SerializeField] private Color colorAtHypomania = new Color32(0xD4, 0x94, 0x3A, 0xFF);

        [Tooltip("Trail color at full depression (state = 0.0). Per the color system:\n" +
                 "cool grey #8A8E96. Drained, low contrast against the grey background.")]
        [SerializeField] private Color colorAtDepression = new Color32(0x8A, 0x8E, 0x96, 0xFF);

        [Tooltip("Alpha taper from head (at the figure) to tail (oldest point).\n" +
                 "Hypomania: long persistent tail; depression: quick fade.\n" +
                 "X axis = trail position (0=newest, 1=oldest), Y axis = alpha.")]
        [SerializeField] private AnimationCurve alphaTaperCurve =
            new AnimationCurve(
                new Keyframe(0f, 1.0f),
                new Keyframe(0.6f, 0.7f),
                new Keyframe(1f, 0f));

        // ═══ Lifetime ════════════════════════════════════════════════════════

        [Header("Lifetime by State")]

        [Tooltip("Seconds before a vertex disappears, at full hypomania.\n" +
                 "Long trail = the gesture persists, the world keeps the record.")]
        [SerializeField, Range(0.5f, 6f)] private float lifetimeAtHypomania = 2.5f;

        [Tooltip("Seconds before a vertex disappears, at full depression.\n" +
                 "Short trail = your motion dissolves as fast as you produce it.")]
        [SerializeField, Range(0.05f, 2f)] private float lifetimeAtDepression = 0.4f;

        // ═══ Smoothing ═══════════════════════════════════════════════════════

        [Header("Smoothing")]

        [Tooltip("How responsively the trail's properties react to state changes.\n" +
                 "Higher = more lag (catches up over a second or so). Important to keep\n" +
                 "this nonzero — abrupt state snaps from the debug HUD shouldn't make\n" +
                 "the trail snap visibly.")]
        [SerializeField, Range(0.05f, 2f)] private float propertySmoothTime = 0.25f;

        // ═══ Internals ═══════════════════════════════════════════════════════

        private TrailRenderer trail;

        private float currentWidth;
        private float widthVelocity;

        private float currentLifetime;
        private float lifetimeVelocity;

        private Color currentHeadColor;
        private Color headColorVelocity;

        // ═══ Unity lifecycle ═════════════════════════════════════════════════

        private void Awake()
        {
            trail = GetComponent<TrailRenderer>();

            // Configure the trail's once-only properties. The rest we drive every frame.
            ConfigureStaticTrailProperties();

            // Initialize live values to whatever state currently is, so we don't see
            // a frame-1 snap from defaults.
            float state = StateValue();
            currentWidth     = Mathf.Lerp(widthAtDepression, widthAtHypomania, state);
            currentLifetime  = Mathf.Lerp(lifetimeAtDepression, lifetimeAtHypomania, state);
            currentHeadColor = Color.Lerp(colorAtDepression, colorAtHypomania, state);

            ApplyTrailProperties();
        }

        private void Update()
        {
            float state = StateValue();

            // Smooth-track the target values. SmoothDamp on each independently so the
            // trail breathes smoothly even when state snaps via the HUD.
            float targetWidth     = Mathf.Lerp(widthAtDepression, widthAtHypomania, state);
            float targetLifetime  = Mathf.Lerp(lifetimeAtDepression, lifetimeAtHypomania, state);
            Color targetHeadColor = Color.Lerp(colorAtDepression, colorAtHypomania, state);

            currentWidth = Mathf.SmoothDamp(
                currentWidth, targetWidth, ref widthVelocity, propertySmoothTime);

            currentLifetime = Mathf.SmoothDamp(
                currentLifetime, targetLifetime, ref lifetimeVelocity, propertySmoothTime);

            // Color smoothing per-channel (no built-in Color SmoothDamp).
            currentHeadColor = new Color(
                Mathf.SmoothDamp(currentHeadColor.r, targetHeadColor.r, ref headColorVelocity.r, propertySmoothTime),
                Mathf.SmoothDamp(currentHeadColor.g, targetHeadColor.g, ref headColorVelocity.g, propertySmoothTime),
                Mathf.SmoothDamp(currentHeadColor.b, targetHeadColor.b, ref headColorVelocity.b, propertySmoothTime),
                Mathf.SmoothDamp(currentHeadColor.a, targetHeadColor.a, ref headColorVelocity.a, propertySmoothTime));

            ApplyTrailProperties();
        }

        // ═══ Static config ═══════════════════════════════════════════════════

        private void ConfigureStaticTrailProperties()
        {
            // Material: TrailRenderer needs an unlit transparent material. We'll create
            // a default one if the user hasn't assigned anything. This keeps the script
            // self-contained — drop it on a GameObject and it just works.
            if (trail.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    trail.material = new Material(shader);
                }
                else
                {
                    Debug.LogWarning(
                        $"[{nameof(PlayerTrail)}] Could not find Sprites/Default shader. " +
                        $"Assign a Material to the TrailRenderer manually.");
                }
            }

            // Smoother corners — Unity's default of 0 makes hard angular pivots.
            trail.numCornerVertices = 4;
            trail.numCapVertices    = 4;

            // Generate vertices any time the figure has moved at all.
            trail.minVertexDistance = 0.04f;

            // Disable autodestruct — we want the trail to live as long as the figure.
            trail.autodestruct = false;
            trail.emitting = true;

            // Anchor the trail to world space; it should not move with the figure
            // (each vertex stays where it was painted).
            trail.alignment = LineAlignment.View;
        }

        // ═══ Per-frame application ═══════════════════════════════════════════

        private void ApplyTrailProperties()
        {
            // Lifetime.
            trail.time = currentLifetime;

            // Width: combine the state-driven max width with the head-to-tail taper curve.
            // We do this by scaling each keyframe of the taper curve by currentWidth and
            // assigning a new AnimationCurve to widthCurve. Allocates a tiny bit per frame
            // but it's a handful of floats — irrelevant.
            Keyframe[] sourceKeys = widthTaperCurve.keys;
            Keyframe[] scaledKeys = new Keyframe[sourceKeys.Length];
            for (int i = 0; i < sourceKeys.Length; i++)
            {
                scaledKeys[i] = new Keyframe(
                    sourceKeys[i].time,
                    sourceKeys[i].value * currentWidth,
                    sourceKeys[i].inTangent,
                    sourceKeys[i].outTangent);
            }
            trail.widthCurve = new AnimationCurve(scaledKeys);

            // Color gradient: head color from state, tail color same hue with alpha taper.
            Gradient g = new Gradient();
            GradientColorKey[] colorKeys = new GradientColorKey[]
            {
                new GradientColorKey(currentHeadColor, 0f),
                new GradientColorKey(currentHeadColor, 1f),
            };

            // Alpha keys driven by the alpha taper curve. We sample the curve at a few
            // points to build the gradient — Gradient supports up to 8 alpha keys.
            const int alphaSampleCount = 6;
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[alphaSampleCount];
            for (int i = 0; i < alphaSampleCount; i++)
            {
                float t = (float)i / (alphaSampleCount - 1);
                float alpha = Mathf.Clamp01(alphaTaperCurve.Evaluate(t)) * currentHeadColor.a;
                alphaKeys[i] = new GradientAlphaKey(alpha, t);
            }
            g.SetKeys(colorKeys, alphaKeys);
            trail.colorGradient = g;
        }

        // ═══ State access ════════════════════════════════════════════════════

        private float StateValue()
        {
            return EmotionalStateManager.Instance != null
                ? EmotionalStateManager.Instance.CurrentValue
                : 0.5f;
        }

        // ═══ Public API ══════════════════════════════════════════════════════

        /// <summary>Stop emitting new trail points without clearing the existing trail.</summary>
        public void PauseEmission()  => trail.emitting = false;

        /// <summary>Resume emitting trail points.</summary>
        public void ResumeEmission() => trail.emitting = true;

        /// <summary>
        /// Wipe the entire trail instantly. Useful for stage transitions, or for
        /// the moment between scenes when we don't want a trail bridging two areas.
        /// </summary>
        public void ClearTrail() => trail.Clear();
    }
}
