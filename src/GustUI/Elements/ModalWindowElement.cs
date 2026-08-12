using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;

namespace GustUI.Elements
{
    [ElementTraits(typeof(BodyTextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnMouseButtonHeldDown))]
    public class ModalWindowElement : FilledRectangleElement
    {
        private ModalTitleBarElement titleBarElement;
        private List<BasicButtonElement> buttons = new List<BasicButtonElement>();
        private FilledRectangleElement buttonBackgroundElement;


        public bool FitModalToContent { get; private set; } = true;

        /// <summary>Whether this modal keeps itself centered in the window
        /// (design-guide.md §9: non-full-screen modals default to centered,
        /// and re-center if the window resizes). Defaults to true UNLESS an
        /// explicit position was passed at construction (treated as an
        /// intentional override). Turns off permanently the moment the user
        /// drags the modal by its title bar — their placement wins from
        /// then on, including across a later resize.</summary>
        public bool AutoCenter { get; set; } = true;

        private Element content;
        public ModalWindowElement()
        {

            titleBarElement = this.AddChildElement<ModalTitleBarElement>();
            buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();

            Setup();
        }
        public ModalWindowElement(string title, string body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            AutoCenter = position == null;
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(BodyForeground));

            Set<BodyTextTrait>(new TVText(body));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));
            Set<BorderFillTrait>(new TVBorder9Grid());


            if (buttons != null)
            {
                this.buttons.AddRange(buttons);
            }
            else
            {
                this.buttons = new List<BasicButtonElement>();
            }

            titleBarElement = new ModalTitleBarElement(
                title,
                this,
                new TVVector(0, 0),
                new TVVector(size != null ? size.X : 400, 40));

            AddChildElement(titleBarElement);
            content = this.AddChildElement<TextElement>();

            content.Set<PositionTrait>(new TVVector(10, 50));
            content.Set<TextTrait>(new TVText(body));
            content.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            content.Set<ForegroundColorTrait>(new TVColor(BodyForeground));
            content.Set<SizeTrait>(new TVVector(size.X - 20, 0));
            content.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            Setup();
        }

        public ModalWindowElement(string title, Element body, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            AutoCenter = position == null;

            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(BodyForeground));

            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(new TVVector(content != null ? content.GetSize().X + 20 : size.X, size.Y) ?? new TVVector(0, 0));

            Set<BorderFillTrait>(new TVBorder9Grid());


            if (buttons != null)
            {
                this.buttons.AddRange(buttons);
            }
            else
            {
                this.buttons = new List<BasicButtonElement>();
            }

            titleBarElement = new ModalTitleBarElement(
                title,
                this,
                new TVVector(0, 0),
                new TVVector(size != null ? size.X : 400, 40));

            AddChildElement(titleBarElement);

            this.content = body;
            this.AddChild(this.content, "content");

            content.Set<PositionTrait>(new TVVector(0, 0));


            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement = this.AddChildElement<FilledRectangleElement>();
            }

            Setup();
        }

        // Themed body/footer fill (was a near-white gradient — a bright
        // dialog box floating in an otherwise all-dark app, like an
        // unstyled browser confirm() box). Matches the app's own panel
        // language rather than the framework's old light-mode default.
        // Reads Theme LIVE (not cached) so a modal built after a theme
        // switch picks up the current palette — design-guide.md §1.
        private static Color BodyFillTop => Resources.StaticResources.Theme.SurfaceRaised;
        private static Color BodyFillBottom => Resources.StaticResources.Theme.SurfacePanel;
        private static Color FooterFillTop => Resources.StaticResources.Theme.SurfaceHeader;
        private static Color FooterFillBottom => Resources.StaticResources.Theme.SurfaceBackdrop;
        private static Color BodyForeground => Resources.StaticResources.Theme.BodyText;

        // ---- open/close transition (design-guide.md §5: subtle chrome
        // easing, ~100-150ms) — fade + a few px slide-up on the modal's own
        // chrome (body/title/footer backgrounds, border/shadow), reusing the
        // nine-grid border as the shadow: it already IS one (baked
        // low-opacity corner/edge textures, Theme.cs's NineGridOpacity),
        // previously only grown on drag. Content (buttons, text) doesn't
        // fade — no subtree-opacity primitive exists in GustUI to do that
        // cheaply, and a chrome-only fade already reads as a real transition.
        private readonly Stopwatch animClock = Stopwatch.StartNew();
        private double closeStartSeconds = -1;
        private bool closing;
        private TVVector restPosition;
        private bool restPositionInitialized;
        private float dragGrowSize = 16f;
        private const float TransitionSeconds = 0.15f;
        private const float SlideDistance = 10f;

        private static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1f - t, 3);
        private static float EaseInCubic(float t) => t * t * t;

        /// <summary>Centered-in-window position (design-guide.md §9), Y
        /// floored at the fruit-menu bar like the pre-existing screen-clamp
        /// logic below — a modal taller than the window shouldn't compute a
        /// negative centered Y and fight that clamp every frame.</summary>
        private TVVector CenteredPosition()
        {
            TVVector windowSize = Resources.StaticResources.RootWindow.GetSize();
            TVVector modalSize = ElementTrait<SizeTrait>().Value();

            float topLimit = 0;
            if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
            {
                topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
            }

            float y = Math.Max(topLimit, (windowSize.Y - modalSize.Y) / 2f);
            return new TVVector((windowSize.X - modalSize.X) / 2f, y);
        }

        /// <summary>Intercepts EVERY removal path (the X button routes here
        /// via ModalTitleBarElement's default Parent.Kill(); a footer button
        /// calling Parent.Kill() directly through a plain Element reference
        /// reaches this too, since Element.Kill() is now virtual) — starts
        /// the close animation instead of removing immediately; Update()
        /// calls base.Kill() once it finishes.</summary>
        public override void Kill()
        {
            if (closing)
            {
                return;
            }

            closing = true;
            closeStartSeconds = animClock.Elapsed.TotalSeconds;
            restPosition = ElementTrait<PositionTrait>().Value();
        }

        /// <summary>Minimum inset between a modal's chrome (title bar,
        /// edges, footer) and its content (design-guide.md §3 — modals
        /// carry at least this much margin; nested containers within that
        /// content are exempt, since the outer inset already separates them
        /// from the modal edge).</summary>
        private const float ContentMargin = 12f;

        private void Setup()
        {
            // Same fix as FullScreenModalElement/FruitMenuElement: a view
            // rebuild (Stage.Clear, ezmuze-studio) kills every non-chrome
            // window child, which used to include an open Preferences/About
            // dialog as collateral damage of an unrelated sequencer rebuild
            // (e.g. adding a channel while the dialog was open) — silently
            // closing it and abandoning any dropdown it had open. Explicit
            // closes (the Close button, Esc) still work: they call Kill()
            // directly, which IsChrome does not intercept — it only exempts
            // this element from the automated Stage.Clear sweep.
            IsChrome = true;

            Set<BackgroundFillTrait>(new TVFillSimpleGradient(BodyFillTop, BodyFillBottom, Direction.Vertically));
            var size = this.GetSize();
            Set<BorderFillTrait>(new TVBorder9Grid());
            restPosition = ElementTrait<PositionTrait>().Value();

            float contentHeight = content is TextElement textElement ? textElement.CalculatedSize().Y : content.GetSize().Y;
            float contentWidth = content is TextElement tx ? tx.CalculatedSize().X : content.GetSize().X;
            float buttonHeight = (this.buttons.Count > 0 ? 80 : ContentMargin);

            float calcHeight = 40 + ContentMargin + contentHeight + ContentMargin + buttonHeight;
            float calcWidth = contentWidth + ContentMargin * 2;
            size = new TVVector(calcWidth, calcHeight);
            titleBarElement.Set<SizeTrait>(new TVVector(size.X, 40));

            Set<SizeTrait>(size);

            if (this.buttons.Count > 0)
            {
                buttonBackgroundElement.Set<PositionTrait>(new TVVector(0, size.Y - 80));
                buttonBackgroundElement.Set<SizeTrait>(new TVVector(size.X, 80));
                buttonBackgroundElement.Set<BackgroundFillTrait>(new TVFillSimpleGradient(FooterFillTop, FooterFillBottom, Direction.Vertically));
                float xPos = size.X - 20;
                int i = 0;
                foreach (var button in this.buttons)
                {
                    i++;
                    button.Parent = buttonBackgroundElement;
                    buttonBackgroundElement.AddChild(button, $"Button {i}");
                    var buttonSize = button.GetSize().X;
                    button.Set<BorderSizeTrait>(new TVInt(0));
                    button.Set<PositionTrait>(new TVVector(xPos - button.GetSize().X, 40 - (button.GetSize().Y / 2f)));
                    Log.This($"Button {i} position: {button.GetActualPosition()} - {xPos}");
                    xPos -= buttonSize + 20;

                }
            }
            Vector2 calculatedModalSize = size.AsXna;
            Vector2 actualPosition = this.GetActualPosition().AsXna;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;

            justSpawned = true;
        }
        private bool justSpawned = false;
        private int maximizeAttemptCount = 0;
        public override void Update(Element parent = null)
        {
            base.Update(parent);

            if (FitModalToContent)
            {
                float contentHeight = content is TextElement textElement ? textElement.CalculatedSize().Y : content.GetSize().Y;
                float contentWidth = content is TextElement tx ? tx.CalculatedSize().X : content.GetSize().X;
                float buttonHeight = (this.buttons.Count > 0 ? 80 : ContentMargin);

                this.Set<SizeTrait>(new TVVector(
                    contentWidth + ContentMargin * 2,
                    40 + ContentMargin + contentHeight + ContentMargin + buttonHeight));
            }

            var size = this.GetSize();

            Vector2 calculatedModalSize = size.AsXna;
            Vector2 actualPosition = this.GetActualPosition().AsXna;
            Vector2 windowSize = Resources.StaticResources.RootWindow.GetSize().AsXna;
            float opacity = BeingDragged ? 0.15f : 0.35f;

            bool limitToScreenSpace = false;

            if (limitToScreenSpace)
            {
                if (actualPosition.X + calculatedModalSize.X > windowSize.X)
                {
                    Set<PositionTrait>(new TVVector(windowSize.X - calculatedModalSize.X, actualPosition.Y));
                }

                if (actualPosition.Y + calculatedModalSize.Y > windowSize.Y)
                {
                    Set<PositionTrait>(new TVVector(actualPosition.X, windowSize.Y - calculatedModalSize.Y));
                }

                float topLimit = 0;
                if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
                {
                    topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
                }

                if (actualPosition.Y < topLimit)
                {
                    Set<PositionTrait>(new TVVector(actualPosition.X, topLimit));
                    
                }

                if (actualPosition.X < 0)
                {
                    Set<PositionTrait>(new TVVector(0, actualPosition.Y));
                }
            }
            else
            {
                float topLimit = 0;
                if (Resources.StaticResources.RootWindow.Children.Items.Any(x => x is FruitMenuElement))
                {
                    topLimit = Resources.StaticResources.RootWindow.Children.Items.First(x => x is FruitMenuElement).GetSize().Y;
                }

                if (!isFullScreen)
                {
                    if (actualPosition.Y < topLimit)
                    {
                        Set<PositionTrait>(new TVVector(actualPosition.X, topLimit));
                        maximizeAttemptCount++;
                        if (maximizeAttemptCount > 10)
                        {
                            ToggleFullScreen();
                            maximizeAttemptCount = 0;
                        }
                    }
                    else
                    {
                        maximizeAttemptCount = 0;
                    }
                }

            }

            if (justSpawned)
            {
                this.MoveToFront();
                justSpawned = false;
            }

            // A drag is the user taking manual control — auto-centering
            // never re-engages for this instance after that (design-guide.md
            // §9), even once they let go.
            if (BeingDragged)
            {
                AutoCenter = false;
            }

            bool autoCenterActive = AutoCenter && !isFullScreen && !BeingDragged;

            // ---- open/close transition (design-guide.md §5) ----
            // First real frame: capture restPosition from whatever the
            // clamp-to-screen-space logic ABOVE just settled on (already ran
            // this same Update() call), not the raw constructor position —
            // a modal whose intended Y sits above the fruit-menu limit (e.g.
            // PreferencesModal's centered-but-tall math) gets clamped there
            // every frame; sliding from the pre-clamp value would fight that
            // clamp for the whole transition and trip the maximizeAttemptCount
            // heuristic above into wrongly full-screening the modal. When
            // AutoCenter is on, skip straight to the real centered position
            // instead, so the very first frame opens already centered.
            if (!restPositionInitialized)
            {
                restPosition = autoCenterActive ? CenteredPosition() : ElementTrait<PositionTrait>().Value();
                restPositionInitialized = true;
            }

            double animElapsed = animClock.Elapsed.TotalSeconds;
            float animProgress;
            if (closing)
            {
                float t = (float)Math.Min(1.0, (animElapsed - closeStartSeconds) / TransitionSeconds);
                animProgress = 1f - EaseInCubic(t);
                if (t >= 1f)
                {
                    base.Kill();
                    return;
                }
            }
            else
            {
                float t = (float)Math.Min(1.0, animElapsed / TransitionSeconds);
                animProgress = EaseOutCubic(t);
                if (t >= 1f)
                {
                    if (autoCenterActive)
                    {
                        // Steady-state, still auto-centered: recompute EVERY
                        // frame (design-guide.md §9 — "if the screen resizes,
                        // that should move the modal to correct it") and
                        // apply directly; the slide-offset block below only
                        // fires mid-transition, so a resize correction needs
                        // its own explicit Set here.
                        restPosition = CenteredPosition();
                        Set<PositionTrait>(restPosition);
                    }
                    else
                    {
                        // Steady-state, not auto-centering (dragged, explicit
                        // position, or full-screen): keep tracking the real
                        // position so a later close-slide starts from
                        // wherever the modal actually is, not spawn.
                        restPosition = ElementTrait<PositionTrait>().Value();
                    }
                }
            }

            if (closing || animProgress < 1f)
            {
                Set<PositionTrait>(new TVVector(restPosition.X, restPosition.Y + (1f - animProgress) * SlideDistance));
            }

            if (ElementTrait<BackgroundFillTrait>().Value() is TVFill bodyFill)
            {
                bodyFill.Opacity = animProgress;
            }

            if (titleBarElement.ElementTrait<BackgroundFillTrait>().Value() is TVFill titleFill)
            {
                titleFill.Opacity = animProgress;
            }

            if (this.buttons.Count > 0 && buttonBackgroundElement.ElementTrait<BackgroundFillTrait>().Value() is TVFill footerFill)
            {
                footerFill.Opacity = animProgress;
            }

            if (ElementTrait<BorderFillTrait>().Value() is TVBorder9Grid nineGrid)
            {
                // The nine-grid border already IS the modal's drop shadow
                // (baked low-opacity corner/edge textures) — grown on drag
                // as before, now ALSO scaled by open/close progress so the
                // shadow visibly deepens as the modal slides into place.
                float dragTarget = BeingDragged ? 32f : 16f;
                dragGrowSize = MathHelper.Lerp(dragGrowSize, dragTarget, 0.2f);
                nineGrid.NineGridSize = dragGrowSize * animProgress;
                nineGrid.Opacity = 0.5f * animProgress;
            }

            //if (isFullScreen)
            //{
            //    Set<SizeTrait>(new TVVector(Resources.StaticResources.RootWindow.GetSize().X, Resources.StaticResources.RootWindow.GetSize().Y - 40));
            //    Set<PositionTrait>(new TVVector(0, 40));
            //}

           
            buttonBackgroundElement.Set<PositionTrait>(new TVVector(0, size.Y - 80));
            buttonBackgroundElement.Set<SizeTrait>(new TVVector(size.X, 80));
            float xPos = size.X - 20;
            foreach (var button in buttonBackgroundElement.Children.Items)
            {
                button.Set<PositionTrait>(new TVVector(xPos - button.GetSize().X, 40 - (button.GetSize().Y / 2f)));
                xPos -= button.GetSize().X + 20;
            }

            //content.Set<PositionTrait>(new TVVector((size.X / 2f) - (content.GetSize().X / 2f), (size.Y / 2f) - (content.GetSize().Y / 2f)));
            // X centering falls out of the size calc above already including
            // ContentMargin on both sides (design-guide.md §3): size.X is
            // contentWidth + 2*margin, so (size.X/2) - (contentWidth/2)
            // resolves to exactly margin either side.
            content.Set<PositionTrait>(new TVVector((size.X / 2f) - (content.GetSize().X / 2f), 40 + ContentMargin));

        }

    }
}
