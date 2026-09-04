using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.View.Shell
{
    /// <summary>
    /// Real browser text entry for the WebGL player, laid over the UI Toolkit field it belongs to.
    ///
    /// A WebGL build cannot open a phone's keyboard. TouchScreenKeyboard is not implemented on
    /// this platform, so a TextField on a phone takes the tap, shows a caret, and then waits for
    /// keys that can never arrive - which made hosting or joining a duel from a phone impossible,
    /// and naming a deck or searching the pool with it.
    ///
    /// It cannot be fixed from inside the player. A browser opens its keyboard only when an input
    /// is focused DURING a user gesture, and Unity processes input inside a requestAnimationFrame
    /// callback, long after the gesture that caused it has ended. Whatever the C# reacts to, by
    /// the time it runs the gesture is over and focus() is ignored.
    ///
    /// So the browser's own input is the thing the finger lands on: one per field, positioned over
    /// it every few frames and styled to match, with the value polled back. The Unity field stays
    /// for layout and for the editor, where it works perfectly well on its own.
    /// </summary>
    public static class WebTextEntry
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int SrdTextCreate();
        [DllImport("__Internal")] static extern void SrdTextConfig(int id, string value, string placeholder, int maxLen);
        [DllImport("__Internal")] static extern void SrdTextPlace(int id, float x, float y, float w, float h, float fontPx);
        [DllImport("__Internal")] static extern void SrdTextHide(int id);
        [DllImport("__Internal")] static extern int SrdTextRead(int id, byte[] into, int max);
        [DllImport("__Internal")] static extern void SrdTextWrite(int id, string value);
        [DllImport("__Internal")] static extern void SrdTextDestroy(int id);

        public const bool Supported = true;
#else
        static int SrdTextCreate() { return 0; }
        static void SrdTextConfig(int id, string value, string placeholder, int maxLen) { }
        static void SrdTextPlace(int id, float x, float y, float w, float h, float fontPx) { }
        static void SrdTextHide(int id) { }
        static int SrdTextRead(int id, byte[] into, int max) { return 0; }
        static void SrdTextWrite(int id, string value) { }
        static void SrdTextDestroy(int id) { }

        public const bool Supported = false;
#endif

        /// <summary>How long a password, deck name or search may be. Long enough for any of the
        /// three; short enough that the poll buffer is a fixed size and never grows.</summary>
        const int MaxLen = 64;

        /// <summary>
        /// Give one field a browser input of its own. A no-op anywhere but the WebGL player.
        ///
        /// <paramref name="input"/> is the field's inner element - the one carrying the colours -
        /// and its TEXT is turned transparent, because the browser's input is drawing the text
        /// from here on and two of them in the same place is a smear rather than a field.
        /// </summary>
        public static void Attach(TextField field, VisualElement input, Action<string> onChange,
                                  string placeholder, float fontPx)
        {
            if (!Supported || field == null) return;

            int id = SrdTextCreate();
            if (id <= 0) return;

            string last = field.value ?? "";
            SrdTextConfig(id, last, placeholder ?? "", MaxLen);

            if (input != null) input.style.color = new Color(0f, 0f, 0f, 0f);
            field.textEdition.placeholder = "";

            var buffer = new byte[MaxLen * 4 + 1];

            // Sixty milliseconds, not every frame. What this does is a rectangle comparison and a
            // string read; a field that follows its own layout ten times a second is indisting-
            // uishable from one that follows it sixty times, and this runs on every screen that
            // has a field on it.
            var tick = field.schedule.Execute(() =>
            {
                var box = field.worldBound;
                bool laidOut = !float.IsNaN(box.x) && box.width > 1f && box.height > 1f;
                if (!laidOut || !IsShowing(field)) { SrdTextHide(id); return; }

                SrdTextPlace(id, box.x, box.y, box.width, box.height, fontPx);

                int n = SrdTextRead(id, buffer, buffer.Length);
                string now = n > 0 ? Encoding.UTF8.GetString(buffer, 0, n) : "";
                if (now == last) return;

                // SetValueWithoutNotify, then call the handler by hand: notifying would loop back
                // through the change callback below and write what we just read.
                last = now;
                field.SetValueWithoutNotify(now);
                if (onChange != null) onChange(now);
            }).Every(60);

            // ...and the other direction, for the times the game sets the value itself - the
            // "suggest" button on the multiplayer screen is the one that matters.
            field.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == last) return;
                last = e.newValue ?? "";
                SrdTextWrite(id, last);
            });

            field.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                tick.Pause();
                SrdTextDestroy(id);
            });
        }

        /// <summary>Whether the field is actually on screen - a hidden panel must not leave a
        /// browser input floating over the game.</summary>
        static bool IsShowing(VisualElement v)
        {
            for (var e = v; e != null; e = e.parent)
            {
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
                if (!e.visible) return false;
            }
            return true;
        }
    }
}
