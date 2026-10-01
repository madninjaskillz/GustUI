using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Pin = GustUI.Elements.ModalWindowElement.WindowPin;

namespace GustUI.Tests
{
    /// <summary>
    /// Waiting dialogs (ezmuze #368): a dialog the app pauses on sits above
    /// every window, front-pinned ones included, with the scrim between it and
    /// everything else; nothing beneath it takes the pointer or the keys; a
    /// dialog opened from one stacks over it. And #369: a back-pinned window
    /// chosen from the View list is unpinned.
    /// </summary>
    public class WaitingDialogTests
    {
        private const int AppPoolMax = 100000;

        private static Element Root()
        {
            var root = new Element();
            root.AddTrait<ChildrenTrait>().Set(new TVElements());
            return root;
        }

        private static Element Add(Element parent, string name, int depth)
        {
            var child = new Element { Depth = depth };
            child.AddTrait<ChildrenTrait>().Set(new TVElements());
            parent.AddChild(child, name);
            return child;
        }

        private static int Window(Pin pin = Pin.Normal)
            => Element.FrontDepth(AppPoolMax, ModalWindowElement.StackFloor(pin), ModalWindowElement.StackCeiling(pin, null));

        // ---- stacking ---------------------------------------------------------

        [Fact]
        public void AWaitingDialogSitsAboveEveryPinGroup()
        {
            int question = ModalWindowElement.NextQuestionDepth(new int[0], null);

            Assert.True(question > Window(Pin.Front));
            Assert.True(question > Window(Pin.Normal));
            Assert.True(question > Window(Pin.Back));

            // ...and so does the scrim beneath it.
            Assert.True(question - 1 > Window(Pin.Front));
        }

        [Fact]
        public void AWaitingDialogSitsAboveTheStatusBarAndBelowToastsAndPopups()
        {
            int question = ModalWindowElement.NextQuestionDepth(new int[0], null);

            Assert.True(question > 150000, "above the status bar and the now-playing bar");
            Assert.True(question < ToastHost.ToastDepth, "toasts stay above it");
            Assert.True(ToastHost.ToastDepth < FruitPopupMenu.PopupDepth, "its own menus stay above toasts");
        }

        [Fact]
        public void AStackedWaitingDialogLandsTwoAboveTheOneItOpenedFrom()
        {
            int first = ModalWindowElement.NextQuestionDepth(new int[0], null);
            int second = ModalWindowElement.NextQuestionDepth(new[] { first }, null);
            int third = ModalWindowElement.NextQuestionDepth(new[] { first, second }, null);

            Assert.Equal(first + 2, second);
            Assert.Equal(second + 2, third);

            // Room for the scrim between each pair.
            Assert.True(second - 1 > first);
        }

        [Fact]
        public void AnExplicitCeilingIsHonoured()
        {
            // The beta gate's dialogs stand on a wall far above everything.
            Assert.Equal(1_000_500, ModalWindowElement.NextQuestionDepth(new int[0], 1_000_500));
            Assert.Equal(1_001_000, ModalWindowElement.NextQuestionDepth(new[] { 1_000_500 }, 1_001_000));
        }

        [Fact]
        public void TheTopQuestionIsTheNewestOfTheOpenOnes()
        {
            Element root = Root();
            Element sequencer = Add(root, "sequencer", Window());
            Element older = Add(root, "file-browser", ModalWindowElement.QuestionDepth);
            Element newer = Add(root, "prompt", ModalWindowElement.QuestionDepth + 2);

            var open = new HashSet<Element> { older, newer };
            Assert.Same(newer, ModalWindowElement.TopQuestion(root.Children.Items, open.Contains));

            // The newer one answered: the older is the one waited on again.
            open.Remove(newer);
            Assert.Same(older, ModalWindowElement.TopQuestion(root.Children.Items, open.Contains));

            open.Clear();
            Assert.Null(ModalWindowElement.TopQuestion(root.Children.Items, open.Contains));
        }

        [Fact]
        public void AWaitingDialogIsTheActiveWindowWhateverWasClickedSince()
        {
            Element root = Root();
            Element dialog = Add(root, "dialog", ModalWindowElement.QuestionDepth);
            Element sequencer = Add(root, "sequencer", Window());

            dialog.MarkBroughtForward();
            sequencer.MarkBroughtForward(); // activated after it, e.g. opened by code

            Assert.Same(sequencer, ModalWindowElement.ActiveWindow(root.Children.Items, _ => false));
            Assert.Same(dialog, ModalWindowElement.ActiveWindow(root.Children.Items, w => ReferenceEquals(w, dialog)));
        }

        // ---- input ------------------------------------------------------------

        [Fact]
        public void EverythingBeneathTheDialogIsBlocked()
        {
            Element root = Root();
            Element sequencer = Add(root, "sequencer", Window());
            Element timeline = Add(sequencer, "timeline", 0);
            Element pinnedPanel = Add(root, "panel", Window(Pin.Front));
            Element statusBar = Add(root, "status-bar", 100000);
            Element scrim = Add(root, "scrim", ModalWindowElement.QuestionDepth - 1);
            Element dialog = Add(root, "dialog", ModalWindowElement.QuestionDepth);

            Assert.True(ModalWindowElement.BlockedBehind(sequencer, dialog, root));
            Assert.True(ModalWindowElement.BlockedBehind(timeline, dialog, root));
            Assert.True(ModalWindowElement.BlockedBehind(pinnedPanel, dialog, root));
            Assert.True(ModalWindowElement.BlockedBehind(statusBar, dialog, root));
            Assert.True(ModalWindowElement.BlockedBehind(root, dialog, root));
        }

        [Fact]
        public void TheDialogAndWhatIsAboveItStayLive()
        {
            Element root = Root();
            Element dialog = Add(root, "dialog", ModalWindowElement.QuestionDepth);
            Element field = Add(dialog, "name-field", 0);
            Element toast = Add(root, "toast", ToastHost.ToastDepth);
            Element menu = Add(root, "dropdown", FruitPopupMenu.PopupDepth);
            Element tooltip = Add(root, "tooltip", 1000000);

            Assert.False(ModalWindowElement.BlockedBehind(dialog, dialog, root));
            Assert.False(ModalWindowElement.BlockedBehind(field, dialog, root));
            Assert.False(ModalWindowElement.BlockedBehind(toast, dialog, root));
            Assert.False(ModalWindowElement.BlockedBehind(menu, dialog, root));
            Assert.False(ModalWindowElement.BlockedBehind(tooltip, dialog, root));
        }

        [Fact]
        public void AStackedDialogBlocksTheOneBeneathIt()
        {
            Element root = Root();
            Element browser = Add(root, "file-browser", ModalWindowElement.QuestionDepth);
            Element browserList = Add(browser, "list", 0);
            Element prompt = Add(root, "prompt", ModalWindowElement.QuestionDepth + 2);

            Assert.True(ModalWindowElement.BlockedBehind(browserList, prompt, root));
            Assert.False(ModalWindowElement.BlockedBehind(browserList, browser, root));
        }

        [Fact]
        public void NothingIsBlockedWithoutADialog()
        {
            Element root = Root();
            Element sequencer = Add(root, "sequencer", Window());
            Assert.False(ModalWindowElement.BlockedBehind(sequencer, null, root));
        }

        [Fact]
        public void AnElementOutsideTheTreeIsNotBlocked()
        {
            Element root = Root();
            Element dialog = Add(root, "dialog", ModalWindowElement.QuestionDepth);
            var detached = new Element();

            Assert.False(ModalWindowElement.BlockedBehind(detached, dialog, root));
        }

        [Theory]
        [InlineData(7, false, 3, 7)]   // nothing waiting: the active scope, as always
        [InlineData(7, true, 3, 3)]    // the dialog's own scope while it waits
        [InlineData(7, true, 0, ModalWindowElement.NoHookScope)] // a dialog with no scope: nothing fires
        [InlineData(0, true, 0, ModalWindowElement.NoHookScope)] // not even the global (base) keys
        public void OnlyTheDialogsShortcutsFireWhileItWaits(int active, bool waiting, int questionScope, int expected)
        {
            Assert.Equal(expected, ModalWindowElement.KeyScopeWhileWaiting(active, waiting, questionScope));
        }

        // ---- the title-bar flash -----------------------------------------------

        [Fact]
        public void TheFlashRisesFallsTwiceAndStops()
        {
            float step = ModalTitleBarElement.FlashStepSeconds;

            Assert.Equal(0f, ModalTitleBarElement.FlashLevel(-0.01));
            Assert.Equal(0f, ModalTitleBarElement.FlashLevel(0));
            Assert.Equal(1f, ModalTitleBarElement.FlashLevel(step - 1e-6), 3);
            Assert.True(ModalTitleBarElement.FlashLevel(step * 1.5) < 1f);
            Assert.Equal(1f, ModalTitleBarElement.FlashLevel(step * 3 - 1e-6), 3);
            Assert.Equal(0f, ModalTitleBarElement.FlashLevel(step * ModalTitleBarElement.FlashPulses * 2));
            Assert.Equal(0f, ModalTitleBarElement.FlashLevel(10));
        }

        // ---- #369 -------------------------------------------------------------

        [Theory]
        [InlineData(Pin.Back, Pin.Normal)]
        [InlineData(Pin.Normal, Pin.Normal)]
        [InlineData(Pin.Front, Pin.Front)]
        public void ChoosingAWindowFromViewUnpinsItFromTheBack(Pin before, Pin after)
        {
            Assert.Equal(after, ModalWindowElement.PinWhenChosenFromList(before));
        }

        [Fact]
        public void AnUnpinnedWindowChosenFromViewComesAboveTheMaximisedSequencer()
        {
            Element root = Root();
            Element panel = Add(root, "panel", Window(Pin.Back));
            Element sequencer = Add(root, "sequencer", Window());
            sequencer.MarkBroughtForward();

            // What View does now: unpin, then bring to the top.
            Pin pin = ModalWindowElement.PinWhenChosenFromList(Pin.Back);
            panel.Depth = Window(pin);
            panel.MarkBroughtForward();

            Assert.Same(panel, root.Children.Items[^1]);
        }

        // #647: the owner's call is "pinned stays on top". A normal window
        // chosen from View stays under a front-pinned one that covers it, and
        // that is said rather than left looking like nothing happened.
        [Fact]
        public void AWindowMostlyUnderAFrontPinnedOneIsReportedAsCovered()
        {
            var panel = (100f, 100f, 400f, 300f);
            var others = new List<(Pin, (float, float, float, float))>
            {
                (Pin.Normal, (0f, 0f, 2000f, 1000f)),
                (Pin.Front, (0f, 0f, 2000f, 1000f)),
            };
            Assert.Equal(1, ModalWindowElement.CoveringPinned(Pin.Normal, panel, others));
        }

        [Fact]
        public void AFrontPinnedChoiceIsNeverReportedAsCovered()
        {
            const Pin chosen = Pin.Front;
            var others = new List<(Pin, (float, float, float, float))> { (Pin.Front, (0f, 0f, 2000f, 1000f)) };
            Assert.Equal(-1, ModalWindowElement.CoveringPinned(chosen, (100f, 100f, 400f, 300f), others));
        }

        [Fact]
        public void APinnedWindowClippingACornerOrANormalOneOverItIsNotReported()
        {
            var panel = (100f, 100f, 400f, 300f);
            var corner = new List<(Pin, (float, float, float, float))> { (Pin.Front, (400f, 300f, 400f, 300f)) };
            Assert.Equal(-1, ModalWindowElement.CoveringPinned(Pin.Normal, panel, corner));
            var normal = new List<(Pin, (float, float, float, float))> { (Pin.Normal, (0f, 0f, 2000f, 1000f)) };
            Assert.Equal(-1, ModalWindowElement.CoveringPinned(Pin.Normal, panel, normal));
        }

        [Fact]
        public void TheNoticeSaysWhatCoversItAndWhatToDo()
        {
            Assert.Equal("Chip - Chip is behind the pinned Sequencer: unpin it to bring Chip - Chip forward",
                ModalWindowElement.BehindPinnedText("Chip - Chip", "Sequencer"));
        }

        [Fact]
        public void ThePinSquareFlashesThreeTimesThenStops()
        {
            double step = ModalTitleBarElement.FlashStepSeconds;
            Assert.True(ModalTitleBarElement.PinFlashLevel(step) > 0.9f);
            Assert.True(ModalTitleBarElement.PinFlashLevel(5 * step) > 0.9f);
            Assert.Equal(0f, ModalTitleBarElement.PinFlashLevel(6 * step + 0.01));
            Assert.Equal(0f, ModalTitleBarElement.PinFlashLevel(-1));
        }
    }
}
