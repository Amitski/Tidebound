// ─────────────────────────────────────────────────────────────────────────────
//  Tidebound — DebugHUD.cs
//
//  An on-screen developer console for the emotional state system. NOT shipped
//  to players. This is the dashboard you keep open while building the game so
//  you can see what the state machine is doing without staring at the Inspector.
//
//  Two halves:
//    • Readouts  — current value, current phase, per-frame trend, mixed flag
//    • Hotkeys   — scrub state, freeze/resume, force phases, toggle mixed
//
//  Why IMGUI (the OnGUI / GUI.* API) instead of UI Toolkit or uGUI Canvas:
//    Zero scene setup. Drops in, just works, doesn't pollute the Hierarchy
//    with Canvas/EventSystem/etc. Perfect for a dev tool we'll delete later.
//    For player-facing UI we'll use uGUI; for dev tools, IMGUI is correct.
//
//  When Phase 0 closes, you can either delete this script or wrap the OnGUI
//  body in `#if UNITY_EDITOR` to strip it from builds. Until then — leave it.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using Tidebound.Core;

namespace Tidebound.DebugTools
{
    public class DebugHUD : MonoBehaviour
    {
        // ═══ Visibility ══════════════════════════════════════════════════════

        [Header("Visibility")]

        [Tooltip("If true, the HUD is drawn on screen. Press the toggle key (default F1)\n" +
                 "during play to flip this without stopping the editor.")]
        [SerializeField] private bool showHUD = true;

        [Tooltip("Key to toggle HUD visibility at runtime.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        // ═══ Layout ══════════════════════════════════════════════════════════

        [Header("Layout")]

        [Tooltip("Top-left position of the HUD panel, in screen pixels.")]
        [SerializeField] private Vector2 panelOrigin = new Vector2(12f, 12f);

        [Tooltip("Width of the HUD panel.")]
        [SerializeField] private float panelWidth = 280f;

        [Tooltip("Font size for the HUD text. Bumped up because Unity's default\n" +
                 "IMGUI text is microscopic on high-DPI displays.")]
        [SerializeField, Range(10, 32)] private int fontSize = 14;

        // ═══ Hotkeys ═════════════════════════════════════════════════════════

        [Header("Hotkeys — State Scrub")]
        [SerializeField] private KeyCode forceHypomaniaKey  = KeyCode.Alpha1;
        [SerializeField] private KeyCode forceStableKey     = KeyCode.Alpha2;
        [SerializeField] private KeyCode forceDepressionKey = KeyCode.Alpha3;
        [SerializeField] private KeyCode forceDeepDepKey    = KeyCode.Alpha4;
        [SerializeField] private KeyCode toggleMixedKey     = KeyCode.M;
        [SerializeField] private KeyCode pauseResumeKey     = KeyCode.P;
        [SerializeField] private KeyCode resetCycleKey      = KeyCode.R;

        [Tooltip("Hold this key while pressing Left/Right to nudge state by ±0.05.\n" +
                 "Useful for finding the exact value at which a mechanic kicks in.")]
        [SerializeField] private KeyCode nudgeModifier      = KeyCode.LeftShift;

        // ═══ Internals ═══════════════════════════════════════════════════════

        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private bool stylesInitialized;

        // ═══ Unity lifecycle ═════════════════════════════════════════════════

        private void Update()
        {
            // Toggle HUD visibility.
            if (Input.GetKeyDown(toggleKey)) showHUD = !showHUD;

            HandleHotkeys();
        }

        // ═══ Hotkey handling ═════════════════════════════════════════════════

        private void HandleHotkeys()
        {
            EmotionalStateManager m = EmotionalStateManager.Instance;
            if (m == null) return;

            // Force-phase shortcuts. Set the value to the midpoint of each phase's range.
            if (Input.GetKeyDown(forceHypomaniaKey))  m.SetValue(0.95f);
            if (Input.GetKeyDown(forceStableKey))     m.SetValue(0.50f);
            if (Input.GetKeyDown(forceDepressionKey)) m.SetValue(0.20f);
            if (Input.GetKeyDown(forceDeepDepKey))    m.SetValue(0.05f);

            // Toggle mixed flag.
            if (Input.GetKeyDown(toggleMixedKey)) m.SetMixed(!m.IsMixed);

            // Pause / Resume the auto-cycle.
            if (Input.GetKeyDown(pauseResumeKey))
            {
                // No public IsRunning getter — we infer state by checking if the
                // value changed last frame. Cheap heuristic: just call Resume after
                // pausing, and Pause otherwise. We track our own flag instead.
                paused = !paused;
                if (paused) m.Pause(); else m.Resume();
            }

            // Reset cycle to fresh hypomania.
            if (Input.GetKeyDown(resetCycleKey)) m.ResetCycle(1f);

            // Hold-Shift + arrow nudges. Useful for hunting the exact threshold
            // where a mechanic flips on/off.
            if (Input.GetKey(nudgeModifier))
            {
                if (Input.GetKeyDown(KeyCode.RightArrow))
                    m.SetValue(Mathf.Clamp01(m.CurrentValue + 0.05f));
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                    m.SetValue(Mathf.Clamp01(m.CurrentValue - 0.05f));
            }
        }

        private bool paused;

        // ═══ HUD rendering ═══════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!showHUD) return;
            EmotionalStateManager m = EmotionalStateManager.Instance;
            if (m == null) return;

            EnsureStyles();

            // Outer box. Background only — children draw text on top.
            float panelHeight = 280f;
            Rect panelRect = new Rect(panelOrigin.x, panelOrigin.y, panelWidth, panelHeight);
            GUI.Box(panelRect, GUIContent.none, boxStyle);

            // Inset content area.
            const float pad = 10f;
            GUILayout.BeginArea(new Rect(
                panelRect.x + pad,
                panelRect.y + pad,
                panelRect.width  - pad * 2f,
                panelRect.height - pad * 2f));

            // ─── Header ───
            GUILayout.Label("TIDEBOUND  •  state debug", headerStyle);
            GUILayout.Space(4f);

            // ─── Live state readout ───
            string trend = m.IsRising ? "▲ rising" : m.IsFalling ? "▼ falling" : "—  steady";
            DrawRow("value",   $"{m.CurrentValue:F3}");
            DrawRow("phase",   $"{m.CurrentPhase}");
            DrawRow("trend",   trend);
            DrawRow("mixed",   m.IsMixed ? "ON" : "off");
            DrawRow("paused",  paused    ? "ON" : "off");

            // Visual bar for the state value — quicker to read than the number.
            GUILayout.Space(6f);
            DrawValueBar(m.CurrentValue);

            // ─── Hotkey legend ───
            GUILayout.Space(10f);
            GUILayout.Label("hotkeys", headerStyle);
            DrawHotkey($"{forceHypomaniaKey}", "force hypomania");
            DrawHotkey($"{forceStableKey}", "force stable");
            DrawHotkey($"{forceDepressionKey}", "force depression");
            DrawHotkey($"{forceDeepDepKey}", "force deep depression");
            DrawHotkey($"{toggleMixedKey}", "toggle mixed");
            DrawHotkey($"{pauseResumeKey}", "pause / resume cycle");
            DrawHotkey($"{resetCycleKey}", "reset cycle");
            DrawHotkey($"shift + ←/→", "nudge ±0.05");
            DrawHotkey($"{toggleKey}", "show / hide this panel");

            GUILayout.EndArea();
        }

        // ═══ Style setup ═════════════════════════════════════════════════════

        private void EnsureStyles()
        {
            if (stylesInitialized) return;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = new Color(0.95f, 0.92f, 0.85f, 1f) },
            };

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.85f, 0.55f, 1f) }, // warm gold
            };

            // Solid background for the panel. Built fresh because GUI.skin.box has
            // a tiled texture that looks ugly at this size.
            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.08f, 0.85f));
            bg.Apply();

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bg },
            };

            stylesInitialized = true;
        }

        // ═══ Row helpers ═════════════════════════════════════════════════════

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(70f));
            GUILayout.Label(value, labelStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawHotkey(string key, string description)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, labelStyle, GUILayout.Width(90f));
            GUILayout.Label(description, labelStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawValueBar(float value01)
        {
            // 4-segment bar visualizing the state float across phases.
            // Coloring matches the conceptual palette: grey at left, gold at right.
            Rect r = GUILayoutUtility.GetRect(panelWidth - 30f, 12f);

            // Background track.
            GUI.color = new Color(0.18f, 0.18f, 0.22f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);

            // Fill width proportional to value.
            Rect fill = new Rect(r.x, r.y, r.width * Mathf.Clamp01(value01), r.height);
            // Lerp fill color from grey to gold matching the background gradient mood.
            GUI.color = Color.Lerp(
                new Color(0.61f, 0.63f, 0.66f, 1f), // depression grey
                new Color(0.96f, 0.91f, 0.78f, 1f), // hypomanic gold
                value01);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);

            // Stable-zone tick marks at 0.4 and 0.6.
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            float lowMark  = r.x + r.width * EmotionalState.STABLE_FLOOR;
            float highMark = r.x + r.width * EmotionalState.STABLE_CEILING;
            GUI.DrawTexture(new Rect(lowMark,  r.y - 2f, 1f, r.height + 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(highMark, r.y - 2f, 1f, r.height + 4f), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }
    }
}
