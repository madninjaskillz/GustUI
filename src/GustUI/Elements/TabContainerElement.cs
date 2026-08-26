using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace GustUI.Elements
{
    /// <summary>
    /// A window that hosts several <see cref="ModalWindowElement"/>s' worth
    /// of content behind one shared tab strip (2026-08-17, tabable-modal
    /// feature) — created by <see cref="Merge"/> when a <see cref="ModalWindowElement.Tabable"/>
    /// window is dropped onto another tabable window's title bar (or onto an
    /// existing container, which just gains one more tab). Extends
    /// <see cref="ModalWindowElement"/> itself so it inherits resize/close/
    /// screen-clamp/dock for free — the base class's own title bar is kept
    /// (title-less, close-less) purely as an inert 40px strip that the tab
    /// strip, added as a later (so higher-drawing) child, fully covers.
    /// </summary>
    public class TabContainerElement : ModalWindowElement
    {
        public const int TabStripHeight = 32;
        private const int TabPaddingX = 12;
        private const int TabCloseSize = 16;
        private const int TabGap = 2;
        private const int OverallCloseWidth = 32;

        private static Color StripFill => Resources.StaticResources.Theme.SurfaceHeader;
        private static Color ActiveTabFill => Resources.StaticResources.Theme.SurfaceRaised;
        private static Color InactiveTabFill => Resources.StaticResources.Theme.SurfaceHeader;
        private static Color TabTextColor => Resources.StaticResources.Theme.BodyText;
        private static Color CloseHoverFill => Resources.StaticResources.Theme.AccentMuteOn;

        /// <summary>Same treatment ModalTitleBarElement gives an inactive
        /// window's title bar (2026-08-17) — a container's real "title bar",
        /// as far as the user can see, IS its tab strip: the base class's
        /// own inert one sits fully covered underneath (see the class doc
        /// comment). Without this a multi-tab container would be the one
        /// window that never visually recedes when it isn't the focused
        /// one.</summary>
        private static Color Desaturate(Color c, float amount)
        {
            byte luminance = (byte)MathHelper.Clamp(c.R * 0.299f + c.G * 0.587f + c.B * 0.114f, 0f, 255f);
            Color gray = new Color(luminance, luminance, luminance, c.A);
            return Color.Lerp(c, gray, MathHelper.Clamp(amount, 0f, 1f));
        }

        private const float InactiveDesaturation = 0.8f;

        private sealed class TabEntry
        {
            public string Title;
            public Element Content;
            public FilledRectangleElement Button;
            public FilledRectangleElement CloseX;
            public float Width;

            /// <summary>The source window's own OnContentRehosted, captured
            /// at merge time (2026-08-17, user request: "inform the window
            /// that it's being closed" — extended the same day to also cover
            /// dissolve/tear-off, not just the close-X, after live testing
            /// found a torn-off tab's owner going stale the same way a
            /// closed one used to) — see ModalWindowElement.OnContentRehosted's
            /// own doc comment for the full mechanics. Invoked with null
            /// when THIS specific tab is actually closed (the per-tab X, or
            /// "close all"); invoked with the REPLACEMENT modal when the tab
            /// survives as a rehosted window instead (DissolveToSingleModal,
            /// EndTabDrag's tear-off) — both also re-wire this same callback
            /// (and OnCloseRequested) onto that replacement, so the chain
            /// keeps working no matter how many more times this happens.</summary>
            public System.Action<ModalWindowElement> OwnerRehostCallback;
        }

        private readonly List<TabEntry> tabs = new List<TabEntry>();
        private int activeIndex;
        private readonly FilledRectangleElement tabStrip;
        private readonly FilledRectangleElement overallClose;

        // ---- tab reorder / tear-off drag state (mirrors StackPanelView's
        // own proven chain-entry reorder-drag shape: an anchor-based grab
        // offset, a live preview slot recomputed from the dragged tab's
        // current content-space X, committed on release) ----
        private int draggingIndex = -1;
        private float dragGrabDx;
        private float dragTabX;
        private float dragMouseY;
        private int previewIndex = -1;

        /// <summary>Vertical distance a tab must be dragged away from the
        /// strip before release tears it off into its own floating window
        /// instead of just reordering it within the strip.</summary>
        private const float TearOffThresholdPx = TabStripHeight * 2f;

        // Deferred close requests (2026-08-17): a per-tab or the overall
        // close button killing this element (or one of its tabs' own
        // content) synchronously from inside ITS OWN click dispatch is the
        // same "don't tear down the element whose click handler is still
        // running" hazard fixed elsewhere this session (ModalWindowElement's
        // own tab-merge commit, the sequencer's close button, AboutModal's
        // buttons) — both close paths just flag here and get consumed at
        // the top of Update() on the next frame instead.
        private bool closeAllRequested;
        private TabEntry closeTabRequested;

        public TabContainerElement(TVVector position, TVVector size)
            : base(
                "",
                new FilledRectangleElement(0, 0, 10, 10, new TVFillSolidColor(Color.Transparent)),
                position: position,
                size: size,
                fitToContent: false,
                resizable: true,
                closable: false,
                minSize: new Vector2(420, 240),
                interactiveTitleBar: false)
        {
            // A container is itself a valid merge target — dragging a third
            // tabable window onto it just adds a tab (see Merge below).
            Tabable = true;

            tabStrip = new FilledRectangleElement((int)0, 0, (int)size.X, TabStripHeight, new TVFillSolidColor(StripFill));
            AddChildElement(tabStrip);
            // Blank-strip-area drag: same title-bar press/release routing
            // every other window's own drag bar already uses (undock-on-
            // grab, dock-commit-on-release) — "the tab bar allows dragging
            // this new container in blank areas." BLANK specifically
            // (2026-08-17, user report: "close button still doesn't work")
            // — tabStrip's own rect spans the FULL strip width, geometrically
            // covering every tab button and the overall-close button too
            // (they're siblings positioned within it, not children of it);
            // GustUI's hit-testing doesn't resolve that overlap by picking
            // one winner — EVERY element whose rect contains the point gets
            // the event, unconditionally. Without this guard, clicking a
            // tab (or "close all") ALSO fired HandleTitleBarPress for the
            // whole container on the same click, which unconditionally
            // captures the pointer — starting a container-drag that
            // swallowed the tab/close button's own release.
            tabStrip.Set<OnMousePress>(new TVEvent<ClickEventArgs>(args =>
            {
                if (IsOverInteractiveStripElement(args.GlobalMousePosition.AsXna))
                {
                    return;
                }

                HandleTitleBarPress(args);
            }));
            tabStrip.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>(args =>
            {
                if (IsOverInteractiveStripElement(args.GlobalMousePosition.AsXna))
                {
                    return;
                }

                HandleTitleBarRelease(args);
            }));

            overallClose = new FilledRectangleElement(0, 0, OverallCloseWidth, TabStripHeight, new TVFillSolidColor(Color.Transparent));
            TextElement closeGlyph = overallClose.AddChildElement<TextElement>();
            closeGlyph.Set<PositionTrait>(new TVVector(0, 0));
            closeGlyph.Set<SizeTrait>(new TVVector(OverallCloseWidth, TabStripHeight));
            closeGlyph.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
            closeGlyph.Set<ForegroundColorTrait>(new TVColor(TabTextColor));
            closeGlyph.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            closeGlyph.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            closeGlyph.Set<TextTrait>(Resources.StaticResources.Theme.Icons.CloseIcon.ToTextTrait());
            overallClose.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((_) => closeAllRequested = true));
            // AddTrait, not Set, for OnEnterTrait specifically — found live
            // (2026-08-17, the user's own first real tab-merge drag):
            // RectangleElement declares OnExitTrait but NOT OnEnterTrait in
            // its own [ElementTraits(...)] set, so a plain FilledRectangleElement
            // (this and `close` in BuildTabButton below — unlike
            // BasicButtonElement, which declares both) throws
            // MissingTraitException the first time Set<OnEnterTrait> runs.
            // Never caught until now because a real tab-merge drag — the
            // only path that ever calls this constructor — can't be
            // scripted through the control API, only driven by hand.
            //
            // Hover-highlight AND the tooltip combined into these same two
            // handlers, rather than a separate TooltipElement.Attach call
            // afterward — Attach's own doc comment says outright that it
            // REPLACES any existing enter/exit handlers on its target, which
            // would have silently thrown away the highlight below (never
            // actually reached before this fix; the constructor always
            // threw first).
            overallClose.AddTrait<OnEnterTrait>().Set(new TVEvent<ClickEventArgs>((args) =>
            {
                overallClose.Set<BackgroundFillTrait>(new TVFillSolidColor(CloseHoverFill));
                TooltipElement.Show("Close all tabs", args.GlobalMousePosition.AsXna);
            }));
            overallClose.Set<OnExitTrait>(new TVEvent<ClickEventArgs>((_) =>
            {
                overallClose.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent));
                TooltipElement.Hide();
            }));
            AddChildElement(overallClose);
        }

        /// <summary>Merges two tabable windows: if <paramref name="target"/>
        /// is already a container, <paramref name="dragged"/> just becomes
        /// one more tab in it (that container's own dock state, if any, is
        /// untouched by AddTab — nothing to transfer here); otherwise a NEW
        /// container is built at <paramref name="target"/>'s own rect ("a
        /// new container the same size as the window that was dragged onto"
        /// — <paramref name="target"/> is the one the drag landed ON, i.e.
        /// the one that stayed put) and both windows become its first two
        /// tabs, target first.
        ///
        /// If <paramref name="target"/> was docked, the new container takes
        /// over that same dock slot (2026-08-17, user request — a docked
        /// window merged into a tab container used to silently end up
        /// floating, since AddTab unconditionally Undock()s whatever it
        /// consumes and nothing ever re-docked the container itself).
        /// <paramref name="dragged"/> is never the source of a dock side to
        /// preserve here — by the time a real drag reaches this point it has
        /// always already been undocked itself (HandleTitleBarPress on
        /// press, or the very drag that got it here).</summary>
        /// <summary>
        /// Makes this container show the tab whose content is
        /// <paramref name="content"/>. False if it holds no such tab.
        /// </summary>
        internal bool Activate(Element content)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (!ReferenceEquals(tabs[i].Content, content))
                {
                    continue;
                }

                activeIndex = i;
                ReflowTabStrip();
                ReflowActiveContent();
                MoveToFront();
                return true;
            }

            return false;
        }

        /// <remarks>
        /// Public (2026-08-26) because an app can want this WITHOUT the drag:
        /// "open the editor for this panel as a tab beside it" is the same
        /// operation a user performs by dropping one title bar on another, and
        /// making the app rebuild it out of AddTab and a hand-made container
        /// would be a second implementation of the docked-side handling above.
        /// </remarks>
        public static void Merge(ModalWindowElement target, ModalWindowElement dragged)
        {
            if (target is TabContainerElement existing)
            {
                existing.AddTab(dragged);
                return;
            }

            DockSide dockedSide = target.DockedSide;
            TVVector position = target.ElementTrait<PositionTrait>().Value();
            TVVector size = target.ElementTrait<SizeTrait>().Value();
            var container = new TabContainerElement(position, size);
            // Both real Tabable hosts today (LoopBrowserPanel, WaveBankPanel)
            // set BottomInset to clear the app's own status bar while
            // docked — a fresh container defaults to 0, which would let a
            // docked container render UNDER the status bar instead of
            // stopping short like either original panel did.
            container.BottomInset = Math.Max(target.BottomInset, dragged.BottomInset);
            Resources.StaticResources.RootWindow.AddChild(container, "tab-container-" + Guid.NewGuid());
            container.AddTab(target);
            container.AddTab(dragged);
            if (dockedSide != DockSide.None)
            {
                container.DockTo(dockedSide);
            }
        }

        /// <summary>Detaches <paramref name="source"/>'s content and hosts
        /// it as one more tab, then kills the now-empty source shell
        /// (its title bar/chrome — the actual hosted view survives,
        /// reparented). Activates the newly added tab.</summary>
        public void AddTab(ModalWindowElement source)
        {
            if (source is TabContainerElement sourceContainer)
            {
                // A container is itself a valid drag SOURCE too — it's
                // Tabable (own constructor) and its blank strip area routes
                // through the same HandleTitleBarPress/Release every other
                // tabable window uses. Found 2026-08-17 (live testing this
                // session's own review): the code below this branch reads
                // `source.Content`, which for a container is just the inert
                // 10x10 placeholder its own base constructor was built with,
                // NOT its real tabs — grabbing that and then killing the
                // container (the old, only path) silently destroyed every
                // tab still parented underneath it. Folding over each of the
                // source's real tabs here instead preserves all of them.
                foreach (TabEntry sourceEntry in sourceContainer.DetachAllTabEntries())
                {
                    AddTabEntry(sourceEntry);
                }

                sourceContainer.Undock();
                sourceContainer.Kill();
                return;
            }

            Element sourceContent = source.Content;
            System.Action<ModalWindowElement> ownerRehostCallback = source.OnContentRehosted;
            sourceContent.Parent?.Children?.Remove(sourceContent);
            AddTabEntry(new TabEntry { Title = source.Title, Content = sourceContent, OwnerRehostCallback = ownerRehostCallback });

            // The shell's own OnCloseRequested/hook-scope etc. no longer
            // apply — Undock() first in case it was mid-dock (Kill()
            // itself, on a still-docked instance, animates instead of
            // instantly removing, which would leave a dead DockLayout
            // registration reserving space for a window with nothing in it
            // for the ~150ms fade).
            source.Undock();
            source.Kill();
        }

        /// <summary>Shared tail of both AddTab paths (a plain modal's own
        /// content, or one of a source container's real tabs): registers
        /// the entry, builds its button, and activates it.</summary>
        private void AddTabEntry(TabEntry entry)
        {
            tabs.Add(entry);
            AddChild(entry.Content, "tab-content-" + (tabs.Count - 1));
            entry.Content.Set<PositionTrait>(new TVVector(0, TabStripHeight));

            BuildTabButton(entry);
            activeIndex = tabs.Count - 1;
            ReflowTabStrip();
            ReflowActiveContent();
        }

        /// <summary>Detaches every one of THIS container's own tabs (content
        /// reparented out, no Kill() on any of it) without touching the
        /// container itself — the merge-a-container-into-another path above
        /// is the only caller; the container shell is killed by the caller
        /// once this returns.</summary>
        private List<TabEntry> DetachAllTabEntries()
        {
            List<TabEntry> detached = new List<TabEntry>(tabs);
            foreach (TabEntry entry in detached)
            {
                entry.Content.Parent?.Children?.Remove(entry.Content);
            }

            tabs.Clear();
            return detached;
        }

        /// <summary>Removes tab <paramref name="index"/>, killing its
        /// content and notifying its owner (with null — really closed) —
        /// the close-X (and "close all") path, permanently discarding the
        /// tab. Contrast <see cref="DetachTab"/>: tear-off keeps the
        /// content alive and notifies with the REPLACEMENT modal instead,
        /// since the tab isn't closing, just moving.</summary>
        /// <summary>Closes ONLY the tab hosting <paramref name="content"/>
        /// (its owner is notified with null, the content killed, the
        /// container dissolving back to a plain window at one tab) —
        /// see ModalWindowElement.CloseContent. Unknown content falls back
        /// to closing the whole container.</summary>
        public override void CloseContent(Element content)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Content == content)
                {
                    RemoveTabAndKillContent(i);
                    return;
                }
            }

            Close();
        }

        private void RemoveTabAndKillContent(int index)
        {
            TabEntry entry = tabs[index];
            entry.OwnerRehostCallback?.Invoke(null);
            entry.Content?.Kill();
            RemoveTabEntry(index);
        }

        /// <summary>Detaches tab <paramref name="index"/>'s content (does
        /// NOT kill it) and removes the tab — the tear-off path. Returns
        /// the whole entry (not just its content) so EndTabDrag can carry
        /// the title AND the owner rehost callback over onto the
        /// replacement floating modal, same as DissolveToSingleModal
        /// already does for the down-to-one-tab case.</summary>
        private TabEntry DetachTab(int index)
        {
            TabEntry entry = tabs[index];
            entry.Content.Parent?.Children?.Remove(entry.Content);
            RemoveTabEntry(index);
            return entry;
        }

        private void RemoveTabEntry(int index)
        {
            TabEntry entry = tabs[index];
            entry.Button?.Kill();
            tabs.RemoveAt(index);

            if (tabs.Count == 0)
            {
                Close();
                return;
            }

            if (tabs.Count == 1)
            {
                // Down to one — dissolve back into a plain window (a
                // container only earns its keep with 2+ tabs).
                DissolveToSingleModal();
                return;
            }

            activeIndex = Math.Clamp(activeIndex >= index ? activeIndex - 1 : activeIndex, 0, tabs.Count - 1);
            ReflowTabStrip();
            ReflowActiveContent();
        }

        /// <summary>The one-tab-left teardown: hands its content back to a
        /// normal <see cref="ModalWindowElement"/> at this container's
        /// current rect, then kills the (now-empty) container shell. If the
        /// container itself was docked, the replacement modal takes over
        /// that same dock slot (2026-08-17 — found live: closing tabs down
        /// to the last one used to always drop it as floating even when the
        /// container had been docked, silently un-docking it as a side
        /// effect of an action that has nothing to do with dragging).</summary>
        private void DissolveToSingleModal()
        {
            TabEntry last = tabs[0];
            last.Button?.Kill();
            last.Content.Parent?.Children?.Remove(last.Content);
            tabs.Clear();

            TVVector position = ElementTrait<PositionTrait>().Value();
            TVVector size = ElementTrait<SizeTrait>().Value();
            DockSide dockedSide = DockedSide;
            if (dockedSide != DockSide.None)
            {
                // Unregister THIS container's dock slot before the
                // replacement modal claims it, so the two are never both
                // registered on the same side in the same frame.
                Undock();
            }

            var modal = new ModalWindowElement(last.Title, last.Content, position: position, size: size,
                fitToContent: false, resizable: true, closable: true, minSize: new Vector2(MinSize.X, MinSize.Y));
            modal.Tabable = true;
            // Same BottomInset transfer Merge() does on the way in (found
            // live testing THIS fix: a dissolved modal rendered too tall,
            // under the status bar, because nothing carried it back out).
            modal.BottomInset = BottomInset;
            // Wires this REPLACEMENT modal into the same owner-rehost chain
            // the original had — see ModalWindowElement.OnContentRehosted's
            // own doc comment. OnCloseRequested (a real, normal close via
            // THIS modal's own title-bar X — nothing to do with tabs
            // anymore) reports "really closed" (null) AND still has to
            // Kill() itself explicitly — setting OnCloseRequested at all
            // bypasses ModalTitleBarElement.RequestClose's own default
            // Parent.Kill() fallback, so skipping the explicit call here
            // would leave the window on screen forever with a title-bar X
            // that visibly does nothing (found live testing THIS exact
            // fix). OnContentRehosted keeps the chain alive in case THIS
            // modal gets merged into another tab and dissolved/torn-off
            // again later. Both are no-ops if the tab never had an owner
            // callback to begin with.
            System.Action<ModalWindowElement> ownerRehostCallback = last.OwnerRehostCallback;
            modal.OnCloseRequested = () =>
            {
                ownerRehostCallback?.Invoke(null);
                modal.Kill();
            };
            modal.OnContentRehosted = ownerRehostCallback;
            ownerRehostCallback?.Invoke(modal);
            Resources.StaticResources.RootWindow.AddChild(modal, "tab-torn-off-" + Guid.NewGuid());
            if (dockedSide != DockSide.None)
            {
                modal.DockTo(dockedSide);
            }

            Close();
        }

        private void BuildTabButton(TabEntry entry)
        {
            var button = new FilledRectangleElement(0, 0, 100, TabStripHeight, new TVFillSolidColor(InactiveTabFill));
            TextElement label = button.AddChildElement<TextElement>();
            label.Set<PositionTrait>(new TVVector(TabPaddingX, 0));
            label.Set<SizeTrait>(new TVVector(140, TabStripHeight));
            label.Set<FontTrait>(Resources.StaticResources.Theme.UiFontSmall);
            label.Set<ForegroundColorTrait>(new TVColor(TabTextColor));
            label.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            label.Set<TextTrait>(new TVText(entry.Title));

            float textWidth = Resources.StaticResources.FontManager.MeasureSdfText(Resources.StaticResources.Theme.UiFontSmall, entry.Title).X;
            entry.Width = TabPaddingX + textWidth + TabPaddingX + TabCloseSize + 6;

            var close = new FilledRectangleElement(0, 0, TabCloseSize, TabCloseSize, new TVFillSolidColor(Color.Transparent));
            TextElement closeGlyph = close.AddChildElement<TextElement>();
            closeGlyph.Set<PositionTrait>(new TVVector(0, 0));
            closeGlyph.Set<SizeTrait>(new TVVector(TabCloseSize, TabCloseSize));
            closeGlyph.Set<FontTrait>(Resources.StaticResources.Theme.AltSymbolFont);
            closeGlyph.Set<ForegroundColorTrait>(new TVColor(TabTextColor));
            closeGlyph.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            closeGlyph.Set<VerticalAlignmentTrait>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });
            closeGlyph.Set<TextTrait>(Resources.StaticResources.Theme.Icons.CloseIcon.ToTextTrait());
            close.Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((_) => closeTabRequested = entry));
            // AddTrait, not Set — see overallClose's own identical fix in
            // the constructor above for why.
            close.AddTrait<OnEnterTrait>().Set(new TVEvent<ClickEventArgs>((_) => close.Set<BackgroundFillTrait>(new TVFillSolidColor(CloseHoverFill))));
            close.Set<OnExitTrait>(new TVEvent<ClickEventArgs>((_) => close.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Transparent))));
            button.AddChild(close, "close");
            entry.CloseX = close;

            button.Set<OnMousePress>(new TVEvent<ClickEventArgs>((args) => BeginTabDrag(entry, args)));
            entry.Button = button;
            AddChild(button, "tab-" + Guid.NewGuid());
        }

        private void ActivateFromClick(TabEntry entry)
        {
            int i = tabs.FindIndex(t => t == entry);
            if (i >= 0 && i != activeIndex && draggingIndex < 0)
            {
                activeIndex = i;
                ReflowTabStrip();
                ReflowActiveContent();
            }
        }

        /// <summary>Whether <paramref name="mouse"/> falls within the
        /// overall-close button or any tab's own button (which includes its
        /// close-X — a child positioned within the button's bounds) — see
        /// the constructor's own doc comment on tabStrip's press/release
        /// wiring for why this matters.</summary>
        private bool IsOverInteractiveStripElement(Vector2 mouse)
        {
            if (Contains(overallClose, mouse))
            {
                return true;
            }

            foreach (TabEntry entry in tabs)
            {
                if (Contains(entry.Button, mouse))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(Element element, Vector2 point)
        {
            Vector2 pos = element.GetActualXnaPosition();
            TVVector size = element.GetSize();
            return point.X >= pos.X && point.X <= pos.X + size.X
                && point.Y >= pos.Y && point.Y <= pos.Y + size.Y;
        }

        // ---------------------------------------------------- reorder

        private void BeginTabDrag(TabEntry entry, ClickEventArgs args)
        {
            int index = tabs.FindIndex(t => t == entry);
            if (index < 0)
            {
                return;
            }

            // A press landing on this tab's own close-X shouldn't start a
            // drag at all (2026-08-17, user report: "close button still
            // doesn't work") — capturing the pointer to `button` here would
            // swallow the close-X's own OnMouseRelease, which (like every
            // other trait on a plain FilledRectangleElement) only ever
            // fires via normal hover-based dispatch, never while something
            // else holds capture. Let the press through untouched; the
            // close-X's own handler does the rest on release.
            if (Contains(entry.CloseX, args.GlobalMousePosition.AsXna))
            {
                return;
            }

            // Before setting draggingIndex below, not after (2026-08-17,
            // user report: "swapping tabs worked once and then stopped").
            // ActivateFromClick's only guard is `draggingIndex < 0` — with
            // the set happening first, that guard was checking a value THIS
            // SAME CALL had just written, so it was always false and a
            // plain click never actually activated anything through this
            // path. The one time it appeared to work was almost certainly
            // ordinary press-to-release mouse jitter crossing into a
            // different preview slot, activating the tab as a side effect
            // of EndTabDrag's OWN reorder-commit (`activeIndex = target`)
            // instead — real reorders (intentional or not) kept "working";
            // a precise, stationary click never did.
            ActivateFromClick(entry);

            args.Element.CapturePointer();
            draggingIndex = index;
            previewIndex = index;
            Vector2 buttonPos = entry.Button.GetActualXnaPosition();
            dragGrabDx = args.GlobalMousePosition.AsXna.X - buttonPos.X;
            dragTabX = TabXFor(index);
            dragMouseY = args.GlobalMousePosition.AsXna.Y;
        }

        /// <summary>Sum of every OTHER tab's width + gap before
        /// <paramref name="index"/> — the dragged tab's own resting content-
        /// space X, same "accumulate widths in order" shape
        /// StackPanelView.PositionEntries already uses for chain entries.</summary>
        private float TabXFor(int index)
        {
            float x = 0f;
            for (int i = 0; i < index; i++)
            {
                x += tabs[i].Width + TabGap;
            }

            return x;
        }

        private int PreviewIndexFor(float contentX, int draggedIndex)
        {
            float x = 0f;
            int slot = 0;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (i == draggedIndex)
                {
                    continue;
                }

                float width = tabs[i].Width;
                if (contentX > x + width / 2f)
                {
                    slot++;
                }

                x += width + TabGap;
            }

            return Math.Clamp(slot, 0, tabs.Count - 1);
        }

        private void ContinueTabDrag(Vector2 mouse)
        {
            Vector2 stripPos = tabStrip.GetActualXnaPosition();
            dragTabX = mouse.X - stripPos.X - dragGrabDx;
            dragMouseY = mouse.Y;
            previewIndex = PreviewIndexFor(dragTabX + tabs[draggingIndex].Width / 2f, draggingIndex);
            ReflowTabStrip();
        }

        /// <summary>Commits the drag: a reorder within the strip, or — if
        /// released far enough above/below it — a tear-off into a new
        /// floating window (2026-08-17, re-added after being pulled
        /// entirely: the ORIGINAL tear-off produced bizarre results,
        /// leaving the tab drag and landing in ANOTHER window's own title-
        /// bar-drag/restore-from-maximize state. Re-implemented from
        /// scratch with everything learned fixing the tab-activation and
        /// close-button bugs alongside it this same session).
        ///
        /// The new floating modal carries over BottomInset (same as
        /// DissolveToSingleModal) and gets wired into the SAME owner-rehost
        /// chain that gives — found live testing THIS reimplementation: a
        /// torn-off tab silently orphaned its owner's own modal reference,
        /// since the owner was only ever told about content getting CLOSED,
        /// not content getting RE-HOSTED in a different modal instance
        /// while staying open. Without this, re-toggling a torn-off
        /// LoopBrowserPanel/WaveBankPanel from its own menu/toolbar entry
        /// built and merged an entirely SECOND copy alongside the still-
        /// live torn-off one instead of reusing it.</summary>
        private void EndTabDrag()
        {
            if (draggingIndex < 0)
            {
                return;
            }

            float stripTop = tabStrip.GetActualXnaPosition().Y;
            bool tornOff = dragMouseY < stripTop - TearOffThresholdPx || dragMouseY > stripTop + TearOffThresholdPx;

            int index = draggingIndex;
            int target = previewIndex;
            draggingIndex = -1;
            previewIndex = -1;

            if (tornOff)
            {
                MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
                TVVector size = ElementTrait<SizeTrait>().Value();
                int bottomInset = BottomInset;
                TabEntry entry = DetachTab(index);

                var modal = new ModalWindowElement(entry.Title, entry.Content,
                    position: new TVVector(mouse.X - size.X / 2f, mouse.Y - TabStripHeight / 2f),
                    size: size, fitToContent: false, resizable: true, closable: true,
                    minSize: new Vector2(MinSize.X, MinSize.Y));
                modal.Tabable = true;
                modal.BottomInset = bottomInset;
                // See DissolveToSingleModal's identical wiring for why
                // OnCloseRequested has to explicitly Kill() itself too, not
                // just notify the owner.
                System.Action<ModalWindowElement> ownerRehostCallback = entry.OwnerRehostCallback;
                modal.OnCloseRequested = () =>
                {
                    ownerRehostCallback?.Invoke(null);
                    modal.Kill();
                };
                modal.OnContentRehosted = ownerRehostCallback;
                ownerRehostCallback?.Invoke(modal);
                Resources.StaticResources.RootWindow.AddChild(modal, "tab-torn-off-" + Guid.NewGuid());
                return;
            }

            if (target != index)
            {
                TabEntry reordered = tabs[index];
                tabs.RemoveAt(index);
                tabs.Insert(target, reordered);
                activeIndex = target;
            }

            ReflowTabStrip();
        }

        // ---------------------------------------------------------- layout

        public override void Update(Element parent = null)
        {
            base.Update(parent);

            if (closeAllRequested)
            {
                closeAllRequested = false;
                // Same owner notification RemoveTabAndKillContent gives a
                // single tab's own close — "close all" was the other path
                // that could silently leave every absorbed owner (e.g.
                // LoopBrowserPanel, WaveBankPanel) thinking it was still
                // open forever.
                foreach (TabEntry entry in tabs)
                {
                    entry.OwnerRehostCallback?.Invoke(null);
                }

                Close();
                return;
            }

            if (closeTabRequested != null)
            {
                TabEntry entry = closeTabRequested;
                closeTabRequested = null;
                int i = tabs.FindIndex(t => t == entry);
                if (i >= 0)
                {
                    RemoveTabAndKillContent(i);
                }

                return;
            }

            if (draggingIndex >= 0)
            {
                MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
                if (mouse.LeftButton == ButtonState.Released)
                {
                    EndTabDrag();
                }
                else
                {
                    ContinueTabDrag(new Vector2(mouse.X, mouse.Y));
                }
            }

            ReflowTabStrip();
            ReflowActiveContent();
        }

        private void ReflowTabStrip()
        {
            TVVector size = ElementTrait<SizeTrait>().Value();
            tabStrip.Set<SizeTrait>(new TVVector(size.X, TabStripHeight));
            overallClose.Set<PositionTrait>(new TVVector(size.X - OverallCloseWidth, 0));

            bool active = ModalWindowElement.IsFrontmostWindow(this);
            tabStrip.Set<BackgroundFillTrait>(new TVFillSolidColor(active ? StripFill : Desaturate(StripFill, InactiveDesaturation)));

            float x = 0f;
            for (int i = 0; i < tabs.Count; i++)
            {
                TabEntry entry = tabs[i];
                bool isDragged = i == draggingIndex;
                float slotX;
                if (isDragged)
                {
                    slotX = dragTabX;
                    entry.Button.Depth = 10;
                }
                else
                {
                    entry.Button.Depth = 0;
                    slotX = x;
                }

                Color tabFill = i == activeIndex ? ActiveTabFill : InactiveTabFill;
                entry.Button.Set<PositionTrait>(new TVVector(slotX, 0));
                entry.Button.Set<SizeTrait>(new TVVector(entry.Width, TabStripHeight));
                entry.Button.Set<BackgroundFillTrait>(new TVFillSolidColor(active ? tabFill : Desaturate(tabFill, InactiveDesaturation)));
                entry.CloseX.Set<PositionTrait>(new TVVector(entry.Width - TabCloseSize - 6, (TabStripHeight - TabCloseSize) / 2f));

                x += entry.Width + TabGap;
            }
        }

        private void ReflowActiveContent()
        {
            if (tabs.Count == 0)
            {
                return;
            }

            TVVector size = ElementTrait<SizeTrait>().Value();
            for (int i = 0; i < tabs.Count; i++)
            {
                Element content = tabs[i].Content;
                bool active = i == activeIndex;
                content.Set<PositionTrait>(new TVVector(0, active ? TabStripHeight : -100000));
                if (active)
                {
                    content.Set<SizeTrait>(new TVVector(size.X, Math.Max(0f, size.Y - TabStripHeight)));
                }
            }
        }
    }
}
