using System;

namespace GustUI.Managers
{
    /// <summary>
    /// The system clipboard, as a pair of hooks the HOST fills in.
    ///
    /// GustUI cannot reach a clipboard itself: on Windows it lives behind
    /// WinForms and needs an STA thread, and in a browser it is an async
    /// permission-gated API that no synchronous key handler can call. Both are
    /// platform concerns, so both are supplied from outside — the same shape
    /// the app already uses for other platform capabilities.
    ///
    /// Unset by default. <see cref="Read"/> returning null simply means paste
    /// does nothing, which is what a host that never wired it up should get —
    /// not a crash in a text field.
    /// </summary>
    public static class ClipboardBridge
    {
        /// <summary>Returns the clipboard's current text, or null when there is
        /// none, when the host hasn't wired this up, or when reading failed.
        /// Must not throw.</summary>
        public static Func<string> Read { get; set; }

        /// <summary>Puts text on the clipboard. Must not throw.</summary>
        public static Action<string> Write { get; set; }

        /// <summary>Clipboard text, or empty. Never throws: a clipboard that is
        /// locked by another process, or held by a host that has since gone
        /// away, is a normal thing to hit and is not worth failing a keystroke
        /// over.</summary>
        public static string GetText()
        {
            try
            {
                return Read?.Invoke() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static void SetText(string text)
        {
            try
            {
                Write?.Invoke(text ?? "");
            }
            catch (Exception)
            {
                // Same reasoning as GetText: a failed copy is not worth an
                // exception escaping into the input loop.
            }
        }
    }
}
