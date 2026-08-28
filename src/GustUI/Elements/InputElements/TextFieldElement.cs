using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GustUI.Elements.InputElements
{
    /// <summary>
    /// A single-line text field with a real editing model: a blinking caret,
    /// a selection, and every key and mouse gesture a field is expected to
    /// answer to.
    ///
    /// Click to focus (focus visuals via the focused/unfocused traits); while
    /// focused the InputManager routes keys here and suppresses keyboard
    /// shortcut hooks. Click to place the caret, drag to select, double-click
    /// to take a word, triple-click to take the lot. Left/Right (Ctrl for
    /// whole words, Shift to extend), Home/End, Backspace and Delete (Ctrl to
    /// take a word at a time), Ctrl+A/C/X/V and Ctrl+Z/Y. Tab moves to the
    /// next field. <see cref="OnTextChanged"/> fires per edit,
    /// <see cref="OnSubmit"/> on Enter, and Escape puts the value back to what
    /// it was when the field was focused and gives up focus.
    ///
    /// The caret and the selection are ordinary child elements rather than
    /// something painted by a Draw override, so they sit in the field's own
    /// clip and paint order for free: selection under the text, caret over it.
    /// </summary>
    [ElementTraits(typeof(TextTrait), typeof(OnFocused), typeof(OnUnfocused), typeof(OnMousePress))]
    public class TextFieldElement : FilledRectangleElement
    {
        internal override bool CanBeInputFocused => true;

        /// <summary>Gap between the field's edge and its text.</summary>
        private const int PadX = 6;

        /// <summary>Caret thickness. Two logical pixels rather than one: at
        /// any DPI scale a hairline can land between two physical pixels and
        /// thin out to nearly nothing, and a caret that faint is a caret the
        /// user has to hunt for.</summary>
        private const int CaretWidth = 2;

        /// <summary>Half a blink cycle — the Windows default. The clock is
        /// restarted on every caret move, so the caret is solid at the moment
        /// of typing and only starts blinking once the field goes quiet.</summary>
        private const double BlinkSeconds = 0.53;

        /// <summary>How deep Ctrl+Z goes. Snapshots are whole strings, but a
        /// field's worth of them is a few kilobytes at the very worst.</summary>
        private const int UndoDepth = 200;

        private readonly TextElement textElement;
        private readonly FilledRectangleElement selectionElement;
        private readonly FilledRectangleElement caretElement;
        private TextElement placeholderElement;

        private string text = "";

        // The selection runs between anchor and caret, in either direction:
        // the CARET is the end that moves (and the end the view scrolls to
        // keep visible), the anchor is where the gesture started. Equal means
        // no selection, which is why one pair of fields covers both.
        private int caret;
        private int anchor;

        // How far the text is scrolled off to the left, in pixels. A field is
        // narrower than its longest plausible value far more often than not,
        // and a value you cannot see the end of is barely a value at all.
        private float scroll;

        // x of every caret position in the current text (see
        // SdfFont.MeasureCaretOffsets), rebuilt only when the text or the font
        // actually changes. Index-to-x and x-to-index are both wanted several
        // times per keystroke and per drag frame.
        private float[] caretOffsets = new float[1];
        private string measuredText;
        private string measuredFamily;
        private float measuredSize = float.MinValue;

        private bool focused;
        private bool dragging;
        private bool caretPlacedByPress;
        private string valueAtFocus = "";

        private readonly Stopwatch caretClock = Stopwatch.StartNew();

        private readonly List<Edit> undoStack = new List<Edit>();
        private readonly List<Edit> redoStack = new List<Edit>();
        private EditKind lastEditKind = EditKind.None;

        // Last laid-out geometry, so the per-frame sync only writes traits
        // (each of which fires change events and dirties the parent's cull
        // cache) when something actually moved.
        private Rectangle lastCaretRect;
        private Rectangle lastSelectionRect;
        private bool lastCaretVisible;
        private bool lastSelectionVisible;

        public Action<string> OnTextChanged;

        /// <summary>Raised when Enter is pressed while focused.</summary>
        public Action<string> OnSubmit;

        /// <summary>Raised when Escape is pressed while focused, after the
        /// value has been put back to what it was when the field gained
        /// focus.</summary>
        public Action<string> OnCancel;

        public int MaxLength { get; set; } = 60;

        public string Text
        {
            get => text;
            set
            {
                string incoming = value ?? "";
                if (incoming == text)
                {
                    return;
                }

                text = incoming;
                textElement.Set<TextTrait>(new TVText(text));

                // A programmatic set is not an edit: it takes no undo entry
                // and raises no OnTextChanged. The caret goes to the end of
                // the new value when the field isn't being typed in, and is
                // merely clamped when it is — a view that re-pushes a value
                // while the user has the field open (a colour picker writing
                // its hex box as the wheel moves) must not throw the caret to
                // the end mid-word.
                if (focused)
                {
                    caret = Math.Min(caret, text.Length);
                    anchor = Math.Min(anchor, text.Length);
                }
                else
                {
                    caret = anchor = text.Length;
                    scroll = 0f;
                }

                ResetUndo();
                caretClock.Restart();
            }
        }

        /// <summary>Prompt shown, dimmed, while the field is empty — "Search",
        /// "project.ezproj". A field with no label beside it has nothing else
        /// to say what belongs in it. Null (the default) costs nothing: the
        /// element behind it isn't created until something asks for one.</summary>
        public string Placeholder
        {
            get => placeholderElement?.ElementTrait<TextTrait>().Value().Text;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    placeholderElement?.Kill();
                    placeholderElement = null;
                    return;
                }

                if (placeholderElement == null)
                {
                    var theme = Resources.StaticResources.Theme;
                    placeholderElement = new TextElement { WordWrap = false };
                    placeholderElement.Set<ForegroundColorTrait>(new TVColor(theme.BodyText * 0.45f));
                    placeholderElement.Set<FontTrait>(Font);
                    placeholderElement.Set<PositionTrait>(new TVVector(PadX, 0));
                    placeholderElement.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });
                    placeholderElement.Set<SizeTrait>(new TVVector(10, 10));

                    // Added before the text so it sits under it in paint
                    // order — they are never both visible, but the field's
                    // own draw order stays predictable either way.
                    AddChild(placeholderElement, "placeholder");
                }

                placeholderElement.Set<TextTrait>(new TVText(value));
            }
        }

        /// <summary>Where the caret sits, 0 to <see cref="Text"/>.Length.</summary>
        public int CaretIndex => caret;

        /// <summary>The selected run, empty when there is no selection.</summary>
        public string SelectedText => text.Substring(SelectionStart, SelectionEnd - SelectionStart);

        private int SelectionStart => Math.Min(caret, anchor);
        private int SelectionEnd => Math.Max(caret, anchor);
        private bool HasSelection => caret != anchor;

        public TextFieldElement()
        {
            var theme = Resources.StaticResources.Theme;

            // A field's text stops at the field's edge. Without this a value
            // longer than the box simply carried on drawing over whatever was
            // beside it — a file name running straight through the Save button
            // next to it, which reads as two broken controls rather than one
            // long value. It is also what lets the view scroll: the text is
            // positioned at a negative offset and the field crops it.
            ClipChildren = true;

            // Child order is paint order (equal Depth keeps insertion order),
            // and this is the whole layering: selection wash, then the text on
            // top of it, then the caret over both.
            selectionElement = new FilledRectangleElement();
            selectionElement.Set<PositionTrait>(new TVVector(PadX, 0));
            selectionElement.Set<SizeTrait>(new TVVector(1, 1));
            // AccentSelection (design-guide.md §6: selected is always the
            // blue) at just over half strength. A selection has to read as a
            // BAND at a glance while the text on it stays legible - a wash
            // faint enough to be tasteful is one you have to hunt for, which
            // is the opposite of what a selection is for.
            selectionElement.Set<BackgroundFillTrait>(new TVFillSolidColor(() => Resources.StaticResources.Theme.AccentSelection) { Opacity = 0.55f });
            selectionElement.Opacity = 0f;
            AddChild(selectionElement, "selection");

            textElement = new TextElement { WordWrap = false };
            textElement.Set<ForegroundColorTrait>(new TVColor(theme.BodyText));
            textElement.Set<PositionTrait>(new TVVector(PadX, 4));
            textElement.Set<FontTrait>(theme.UiFont);
            textElement.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });
            textElement.Set<TextTrait>(new TVText(""));
            AddChild(textElement, "text");

            caretElement = new FilledRectangleElement();
            caretElement.Set<PositionTrait>(new TVVector(PadX, 0));
            caretElement.Set<SizeTrait>(new TVVector(CaretWidth, 1));
            caretElement.Set<BackgroundFillTrait>(new TVFillSolidColor(() => Resources.StaticResources.Theme.BodyText));
            caretElement.Opacity = 0f;
            AddChild(caretElement, "caret");

            // Themed idle state (design-guide.md §1) — was hardcoded
            // Color.Gray/Color.Black regardless of app theme, which read as a
            // stray light-mode control inside an otherwise dark app.
            this.Set<BorderFillTrait>(new TVBorderColorFill(theme.SurfaceBorder));
            this.Set<BorderSizeTrait>(new TVInt(1));
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(theme.SurfaceRaised));

            this.Set<OnFocused>(new TVEvent<TVEventArgs>(x => OnFocusedHandler(x)));
            this.Set<OnUnfocused>(new TVEvent<TVEventArgs>(x => OnUnfocusedHandler(x)));
            this.Set<OnMousePress>(new TVEvent<ClickEventArgs>(OnPressHandler));
            this.Set<OnMouseButtonHeldDown>(new TVEvent<ClickEventArgs>(OnDragHandler));
            this.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(_ => dragging = false));
        }

        /// <summary>
        /// The face the field draws its text in. Defaults to the theme's BODY
        /// font, which is right for a dialog and too big for a dense form: an
        /// inspector row gave its value the body face beside a small-face
        /// label, so the value both outsized its own caption and overflowed the
        /// row it was given, clipped along the bottom of every glyph.
        /// </summary>
        public TVFont Font
        {
            get => textElement.ElementTrait<FontTrait>().Value();
            set
            {
                textElement.Set<FontTrait>(value);
                placeholderElement?.Set<FontTrait>(value);
            }
        }

        /// <summary>
        /// Kept for callers that size a field and then ask it to fit its text.
        /// Layout is worked out every frame now (see <see cref="Update"/>) —
        /// it has to be, since the caret and the scroll offset both depend on
        /// the field's width — so this only forces one pass early, before the
        /// first frame draws.
        /// </summary>
        public void FitText() => SyncLayout();

        public void SelectAll()
        {
            anchor = 0;
            caret = text.Length;
            caretClock.Restart();
        }

        /// <summary>Puts the caret at <paramref name="index"/>, dropping any
        /// selection (or extending it, when <paramref name="extend"/>).</summary>
        public void SetCaret(int index, bool extend = false)
        {
            caret = Math.Clamp(index, 0, text.Length);
            if (!extend)
            {
                anchor = caret;
            }

            caretClock.Restart();
        }

        public override void Update(Element parent = null)
        {
            SyncLayout();
            base.Update(parent);
        }

        // ---- layout -------------------------------------------------------

        /// <summary>Rebuilds <see cref="caretOffsets"/> when the string or the
        /// font it was measured against has changed underneath it.</summary>
        private void SyncMetrics()
        {
            TVFont font = Font;
            if (measuredText == text && measuredFamily == font.Family && measuredSize == font.Size)
            {
                return;
            }

            caretOffsets = Resources.StaticResources.FontManager
                .LoadSdfFont(font.Family)
                .MeasureCaretOffsets(text, font.Size);
            measuredText = text;
            measuredFamily = font.Family;
            measuredSize = font.Size;
        }

        private float TextWidth => caretOffsets[caretOffsets.Length - 1];

        private void SyncLayout()
        {
            SyncMetrics();

            TVVector size = this.GetSize();
            int width = Math.Max(1, (int)size.X);
            int height = Math.Max(1, (int)size.Y);
            float lineHeight = Font.Size;
            float innerWidth = Math.Max(1f, width - (PadX * 2));

            // The text sits on the field's vertical centre line rather than a
            // fixed inset, so one field shape works for a 20px inspector row
            // and a 34px dialog field alike — and so the caret, which is drawn
            // to the same line box, agrees with it at every size.
            int textTop = Math.Max(1, (int)Math.Round((height - lineHeight) / 2f));

            ClampCaretIntoView(innerWidth);

            int textLeft = (int)Math.Round(PadX - scroll);
            SetPosition(textElement, textLeft, textTop);

            // Sized to the WHOLE string, not the visible window: the element's
            // bounds are what the parent's visibility cull tests, and a scrolled
            // box whose bounds have slid off to the left gets culled — the text
            // vanishing entirely at exactly the point it starts scrolling.
            SetSize(textElement, (int)Math.Max(innerWidth, TextWidth), (int)Math.Ceiling(lineHeight));

            if (placeholderElement != null)
            {
                SetPosition(placeholderElement, PadX, textTop);
                placeholderElement.Opacity = text.Length == 0 ? 1f : 0f;
            }

            bool showSelection = focused && HasSelection;
            if (showSelection)
            {
                float left = PadX + caretOffsets[SelectionStart] - scroll;
                float right = PadX + caretOffsets[SelectionEnd] - scroll;
                left = Math.Max(left, PadX);
                right = Math.Min(right, width - PadX);
                showSelection = right > left;
                if (showSelection)
                {
                    SetRect(selectionElement, ref lastSelectionRect,
                        (int)left, textTop, Math.Max(1, (int)Math.Round(right - left)), (int)Math.Ceiling(lineHeight));
                }
            }

            SetVisible(selectionElement, ref lastSelectionVisible, showSelection);

            bool showCaret = focused && CaretBlinkOn();
            if (showCaret)
            {
                int caretX = (int)Math.Round(PadX + caretOffsets[caret] - scroll);
                SetRect(caretElement, ref lastCaretRect,
                    Math.Clamp(caretX, PadX - CaretWidth, width - CaretWidth), textTop, CaretWidth, (int)Math.Ceiling(lineHeight));
            }

            SetVisible(caretElement, ref lastCaretVisible, showCaret);
        }

        private bool CaretBlinkOn() => (int)(caretClock.Elapsed.TotalSeconds / BlinkSeconds) % 2 == 0;

        /// <summary>Scrolls the view the least amount that brings the caret
        /// back inside the box, then pulls it back so there is never a gap
        /// after the end of a string short enough to fit.</summary>
        private void ClampCaretIntoView(float innerWidth)
        {
            float caretX = caretOffsets[Math.Min(caret, caretOffsets.Length - 1)];

            if (caretX - scroll > innerWidth - CaretWidth)
            {
                scroll = caretX - innerWidth + CaretWidth;
            }

            if (caretX - scroll < 0f)
            {
                scroll = caretX;
            }

            float maxScroll = Math.Max(0f, TextWidth - innerWidth + CaretWidth);
            scroll = Math.Clamp(scroll, 0f, maxScroll);
        }

        private static void SetPosition(Element element, int x, int y)
        {
            TVVector current = element.CachedPositionTrait.Value();
            if ((int)current.X != x || (int)current.Y != y)
            {
                element.Set<PositionTrait>(new TVVector(x, y));
            }
        }

        private static void SetSize(Element element, int width, int height)
        {
            TVVector current = element.CachedSizeTrait.Value();
            if ((int)current.X != width || (int)current.Y != height)
            {
                element.Set<SizeTrait>(new TVVector(width, height));
            }
        }

        private static void SetRect(Element element, ref Rectangle last, int x, int y, int width, int height)
        {
            var rect = new Rectangle(x, y, width, height);
            if (rect == last)
            {
                return;
            }

            last = rect;
            SetPosition(element, x, y);
            SetSize(element, width, height);
        }

        private static void SetVisible(Element element, ref bool last, bool visible)
        {
            if (last != visible)
            {
                last = visible;
                element.Opacity = visible ? 1f : 0f;
            }
        }

        // ---- mouse --------------------------------------------------------

        /// <summary>
        /// A press places the caret where it landed; a second and third press
        /// in the same spot take the word and then the whole value, the way
        /// every other text field on the machine behaves.
        ///
        /// This runs BEFORE the InputManager assigns focus (both happen on the
        /// same press edge), which is what <see cref="caretPlacedByPress"/> is
        /// for: the focus handler must leave a caret this put somewhere alone,
        /// while still selecting everything when focus arrived by Tab.
        /// </summary>
        private void OnPressHandler(ClickEventArgs e)
        {
            caretPlacedByPress = true;
            SyncMetrics();

            int index = IndexAt(e.GlobalMousePosition.X);
            if (e.ClickCount >= 3)
            {
                SelectAll();
                return;
            }

            if (e.ClickCount == 2)
            {
                SelectWordAt(index);
                return;
            }

            KeyboardState keyboard = Resources.StaticResources.InputManager.CurrentKeyboardState;
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            SetCaret(index, shift);

            // Captured, so a selection drag that leaves the field (or runs
            // over the button next to it) keeps coming back here.
            dragging = true;
            CapturePointer();
        }

        private void OnDragHandler(ClickEventArgs e)
        {
            if (dragging)
            {
                SetCaret(IndexAt(e.GlobalMousePosition.X), extend: true);
            }
        }

        /// <summary>The caret position nearest <paramref name="globalX"/> —
        /// nearest BOUNDARY, not the character under the pointer, so clicking
        /// the right half of a glyph puts the caret after it.</summary>
        private int IndexAt(float globalX)
        {
            float local = globalX - this.GetActualXnaPosition().X - PadX + scroll;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < caretOffsets.Length; i++)
            {
                float distance = Math.Abs(caretOffsets[i] - local);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        // ---- keyboard -----------------------------------------------------

        internal override void HandleKeyInput(Keys key, bool shift, bool control)
        {
            switch (key)
            {
                case Keys.Left:
                    if (HasSelection && !shift && !control)
                    {
                        SetCaret(SelectionStart);
                    }
                    else
                    {
                        SetCaret(control ? PreviousWord(caret) : caret - 1, shift);
                    }

                    return;

                case Keys.Right:
                    if (HasSelection && !shift && !control)
                    {
                        SetCaret(SelectionEnd);
                    }
                    else
                    {
                        SetCaret(control ? NextWord(caret) : caret + 1, shift);
                    }

                    return;

                // A single-line field has nowhere to go up or down, so the
                // vertical keys land on the ends of the line — what a Win32
                // single-line edit does with them too.
                case Keys.Home:
                case Keys.Up:
                    SetCaret(0, shift);
                    return;

                case Keys.End:
                case Keys.Down:
                    SetCaret(text.Length, shift);
                    return;

                case Keys.Back:
                    DeleteBackwards(control);
                    return;

                case Keys.Delete:
                    DeleteForwards(control);
                    return;

                case Keys.Enter:
                    OnSubmit?.Invoke(text);
                    return;

                case Keys.Escape:
                    Cancel();
                    return;

                case Keys.Tab:
                    Resources.StaticResources.InputManager.FocusNext(shift);
                    return;
            }

            if (control)
            {
                HandleShortcut(key, shift);
                return;
            }

            char? c = CharFor(key, shift);
            if (c.HasValue)
            {
                Insert(c.Value.ToString());
            }
        }

        /// <summary>
        /// Ctrl+A / C / X / V and Ctrl+Z / Y.
        ///
        /// Paste is the one that matters most: this field is where API keys,
        /// OAuth client secrets and authorisation codes get entered, and none
        /// of those are things anyone types by hand. Without it those flows are
        /// effectively unusable (reported 2026-08-23).
        ///
        /// Copy and cut act on the SELECTION now that there is one, and do
        /// nothing when it is empty — the same as every other field, and the
        /// reason Ctrl+A had to stop being a no-op.
        /// </summary>
        private void HandleShortcut(Keys key, bool shift)
        {
            switch (key)
            {
                case Keys.A:
                    SelectAll();
                    break;

                case Keys.C:
                    if (HasSelection)
                    {
                        Managers.ClipboardBridge.SetText(SelectedText);
                    }

                    break;

                case Keys.X:
                    if (HasSelection)
                    {
                        Managers.ClipboardBridge.SetText(SelectedText);
                        ReplaceSelection("", EditKind.Other);
                    }

                    break;

                case Keys.V:
                    Insert(Managers.ClipboardBridge.GetText());
                    break;

                case Keys.Z:
                    if (shift)
                    {
                        Redo();
                    }
                    else
                    {
                        Undo();
                    }

                    break;

                case Keys.Y:
                    Redo();
                    break;
            }
        }

        /// <summary>Replaces the selection with <paramref name="incoming"/>
        /// (or inserts at the caret when there is none), honouring
        /// <see cref="MaxLength"/> and stripping anything that can't live on
        /// one line — pasted text routinely arrives with a trailing newline,
        /// and a redirect URL copied out of a browser can arrive with leading
        /// whitespace.</summary>
        public void Insert(string incoming)
        {
            if (string.IsNullOrEmpty(incoming))
            {
                return;
            }

            var cleaned = new StringBuilder(incoming.Length);
            foreach (char c in incoming)
            {
                if (c == '\n' || c == '\r' || c == '\t')
                {
                    continue;
                }

                if (!char.IsControl(c))
                {
                    cleaned.Append(c);
                }
            }

            if (cleaned.Length == 0)
            {
                return;
            }

            // The selection is about to go, so the room a paste has is what's
            // left once it has: replacing a whole 60-character field with a
            // 60-character paste has to fit.
            int room = MaxLength - (text.Length - (SelectionEnd - SelectionStart));
            if (room <= 0)
            {
                return;
            }

            string addition = cleaned.Length <= room ? cleaned.ToString() : cleaned.ToString(0, room);
            ReplaceSelection(addition, addition.Length == 1 ? EditKind.Typing : EditKind.Other);
        }

        private void DeleteBackwards(bool wholeWord)
        {
            if (HasSelection)
            {
                ReplaceSelection("", EditKind.Deleting);
                return;
            }

            if (caret == 0)
            {
                return;
            }

            anchor = wholeWord ? PreviousWord(caret) : caret - 1;
            ReplaceSelection("", EditKind.Deleting);
        }

        private void DeleteForwards(bool wholeWord)
        {
            if (HasSelection)
            {
                ReplaceSelection("", EditKind.Deleting);
                return;
            }

            if (caret == text.Length)
            {
                return;
            }

            anchor = wholeWord ? NextWord(caret) : caret + 1;
            ReplaceSelection("", EditKind.Deleting);
        }

        /// <summary>The single edit primitive: everything that changes the
        /// value — typing, pasting, both deletes, cut — goes through here, so
        /// undo, the caret, the change event and the redraw are handled once
        /// rather than at each call site.</summary>
        private void ReplaceSelection(string replacement, EditKind kind)
        {
            int start = SelectionStart;
            int end = SelectionEnd;
            if (start == end && replacement.Length == 0)
            {
                return;
            }

            PushUndo(kind);

            text = text.Substring(0, start) + replacement + text.Substring(end);
            textElement.Set<TextTrait>(new TVText(text));
            caret = anchor = start + replacement.Length;
            caretClock.Restart();

            OnTextChanged?.Invoke(text);
        }

        // ---- words --------------------------------------------------------

        // Word motion classifies each character as word / whitespace / other,
        // and a run of one class is one hop. Ctrl+Left through "file_v2.wav"
        // therefore stops at the dot rather than treating the whole name as
        // one atom, which is the behaviour that makes the shortcut worth
        // having on the paths and identifiers this app's fields hold.
        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private int PreviousWord(int from)
        {
            int i = Math.Clamp(from, 0, text.Length);
            while (i > 0 && char.IsWhiteSpace(text[i - 1]))
            {
                i--;
            }

            if (i == 0)
            {
                return 0;
            }

            bool word = IsWordChar(text[i - 1]);
            while (i > 0 && !char.IsWhiteSpace(text[i - 1]) && IsWordChar(text[i - 1]) == word)
            {
                i--;
            }

            return i;
        }

        private int NextWord(int from)
        {
            int i = Math.Clamp(from, 0, text.Length);
            if (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                bool word = IsWordChar(text[i]);
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && IsWordChar(text[i]) == word)
                {
                    i++;
                }
            }

            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            return i;
        }

        private void SelectWordAt(int index)
        {
            if (text.Length == 0)
            {
                return;
            }

            // A double-click between two words takes the one BEFORE the gap,
            // which is where the pointer visually is.
            int at = Math.Clamp(index, 0, text.Length - 1);
            if (at > 0 && (at == text.Length || char.IsWhiteSpace(text[at])))
            {
                at--;
            }

            bool white = char.IsWhiteSpace(text[at]);
            bool word = IsWordChar(text[at]);

            int start = at;
            while (start > 0 && char.IsWhiteSpace(text[start - 1]) == white && IsWordChar(text[start - 1]) == word)
            {
                start--;
            }

            int end = at;
            while (end < text.Length && char.IsWhiteSpace(text[end]) == white && IsWordChar(text[end]) == word)
            {
                end++;
            }

            anchor = start;
            caret = end;
            caretClock.Restart();
        }

        // ---- undo ---------------------------------------------------------

        private enum EditKind
        {
            None,
            Typing,
            Deleting,
            Other,
        }

        private readonly struct Edit
        {
            public Edit(string text, int caret, int anchor)
            {
                Text = text;
                Caret = caret;
                Anchor = anchor;
            }

            public string Text { get; }
            public int Caret { get; }
            public int Anchor { get; }
        }

        /// <summary>
        /// Snapshots the value before an edit changes it.
        ///
        /// A RUN of the same kind of edit coalesces into one entry, so Ctrl+Z
        /// takes back the word just typed rather than one letter of it — an
        /// undo stack with one entry per keystroke is technically correct and
        /// useless to press.
        /// </summary>
        private void PushUndo(EditKind kind)
        {
            bool coalesce = kind != EditKind.Other && kind == lastEditKind && undoStack.Count > 0;
            lastEditKind = kind;
            redoStack.Clear();

            if (coalesce)
            {
                return;
            }

            undoStack.Add(new Edit(text, caret, anchor));
            if (undoStack.Count > UndoDepth)
            {
                undoStack.RemoveAt(0);
            }
        }

        private void ResetUndo()
        {
            undoStack.Clear();
            redoStack.Clear();
            lastEditKind = EditKind.None;
        }

        private void Undo()
        {
            if (undoStack.Count == 0)
            {
                return;
            }

            Edit previous = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            redoStack.Add(new Edit(text, caret, anchor));
            Restore(previous);
        }

        private void Redo()
        {
            if (redoStack.Count == 0)
            {
                return;
            }

            Edit next = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            undoStack.Add(new Edit(text, caret, anchor));
            Restore(next);
        }

        private void Restore(Edit edit)
        {
            text = edit.Text;
            textElement.Set<TextTrait>(new TVText(text));
            caret = Math.Clamp(edit.Caret, 0, text.Length);
            anchor = Math.Clamp(edit.Anchor, 0, text.Length);

            // Every undo starts a fresh run — otherwise the next keystroke
            // coalesces into the entry that was just taken off the stack.
            lastEditKind = EditKind.None;
            caretClock.Restart();
            OnTextChanged?.Invoke(text);
        }

        /// <summary>Escape: back to the value the field was focused with, and
        /// give up focus. Two presses therefore back out of a field and then
        /// out of the dialog holding it, since shortcut hooks (which is where
        /// a modal's own Escape lives) are suppressed while a field has
        /// focus.</summary>
        private void Cancel()
        {
            if (text != valueAtFocus)
            {
                text = valueAtFocus;
                textElement.Set<TextTrait>(new TVText(text));
                caret = anchor = text.Length;
                ResetUndo();
                OnTextChanged?.Invoke(text);
            }

            OnCancel?.Invoke(text);
            Resources.StaticResources.InputManager.ClearFocus();
        }

        /// <summary>
        /// The character a key produces. Covers the full printable ASCII range
        /// a US layout can reach, not just letters and digits — the previous
        /// set (a-z, 0-9, space, -, _, .) could not type a URL, a path, or most
        /// API keys, so a field that couldn't be pasted into couldn't be filled
        /// in at all.
        ///
        /// This is a US-layout map. A proper fix is the platform's own
        /// character input (KNI exposes a TextInput event on desktop), which
        /// would also bring dead keys and IME along; that is a bigger change
        /// than this one, and paste covers the case that actually hurts.
        /// </summary>
        private static char? CharFor(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                return shift ? char.ToUpperInvariant(c) : c;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                int digit = key - Keys.D0;
                if (!shift)
                {
                    return (char)('0' + digit);
                }

                return ")!@#$%^&*("[digit];
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return (char)('0' + (key - Keys.NumPad0));
            }

            return key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPlus => shift ? '+' : '=',
                Keys.OemPeriod => shift ? '>' : '.',
                Keys.OemComma => shift ? '<' : ',',
                Keys.OemQuestion => shift ? '?' : '/',
                Keys.OemSemicolon => shift ? ':' : ';',
                Keys.OemQuotes => shift ? '"' : '\'',
                Keys.OemTilde => shift ? '~' : '`',
                Keys.OemOpenBrackets => shift ? '{' : '[',
                Keys.OemCloseBrackets => shift ? '}' : ']',
                Keys.OemPipe or Keys.OemBackslash => shift ? '|' : '\\',
                Keys.Decimal => '.',
                Keys.Add => '+',
                Keys.Subtract => '-',
                Keys.Multiply => '*',
                Keys.Divide => '/',
                _ => (char?)null,
            };
        }

        // design-guide.md §6: a focused element gets a distinct visible
        // indicator (2px AccentSelection outline) separate from hover/press —
        // the field's background also lifts to SurfaceHeader so focus reads
        // even for someone not sensitive to the border color alone.
        private void OnFocusedHandler(TVEventArgs x)
        {
            var theme = Resources.StaticResources.Theme;
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(theme.SurfaceHeader));
            this.Set<BorderFillTrait>(new TVBorderColorFill(theme.AccentSelection));
            this.Set<BorderSizeTrait>(new TVInt(2));

            focused = true;
            valueAtFocus = text;
            ResetUndo();
            caretClock.Restart();

            // Focus that did NOT come from a click — Tab, or a view focusing a
            // field as it opens — selects the whole value, so the first
            // keystroke replaces it. That is what makes tabbing through a form
            // of existing values worth doing.
            if (!caretPlacedByPress)
            {
                SelectAll();
            }

            caretPlacedByPress = false;
        }

        private void OnUnfocusedHandler(TVEventArgs x)
        {
            var theme = Resources.StaticResources.Theme;
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(theme.SurfaceRaised));
            this.Set<BorderFillTrait>(new TVBorderColorFill(theme.SurfaceBorder));
            this.Set<BorderSizeTrait>(new TVInt(1));

            focused = false;
            dragging = false;
            caretPlacedByPress = false;

            // The view goes back to the start of the value: a field showing
            // the tail end of a path it was scrolled to while editing tells
            // the reader far less than one showing its beginning.
            caret = anchor = 0;
            scroll = 0f;
        }
    }
}
