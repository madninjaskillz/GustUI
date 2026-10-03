using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// A file dropped on the app goes to the window drawn on top where it
    /// landed, and inside it to the deepest registrant for its type; a window
    /// that takes nothing of that type does not let it through to the one
    /// behind (ezmuze #683).
    /// </summary>
    public class FileDropRoutingTests
    {
        private readonly Element root;
        private readonly List<string> dropped = new();

        public FileDropRoutingTests()
        {
            root = Box(0, 0, 2560, 1345);
            root.AddTrait<ChildrenTrait>().Set(new TVElements());
        }

        private static Element Box(float x, float y, float w, float h)
        {
            var element = new Element();
            element.AddTrait<PositionTrait>().Set(new TVVector(x, y));
            element.AddTrait<SizeTrait>().Set(new TVVector(w, h));
            element.AddTrait<ChildrenTrait>().Set(new TVElements());
            return element;
        }

        private Element Window(string name, float x, float y, float w, float h)
        {
            Element window = Box(x, y, w, h);
            window.Depth = ModalWindowElement.WindowDepth;
            root.AddChild(window, name);
            return window;
        }

        /// <summary>A child at (x, y) relative to <paramref name="parent"/>.</summary>
        private static Element Child(Element parent, string name, float x, float y, float w, float h)
        {
            Element child = Box(x, y, w, h);
            parent.AddChild(child, name);
            return child;
        }

        private void Register(Element element, string label, params string[] extensions)
            => element.AddTrait<FileDropTrait>().Set(TVFileDrop.For(extensions, _ => dropped.Add(label)));

        private Element WindowAt(float x, float y)
            => ModalWindowElement.TopmostWindowAt(root.Children.Items, new Vector2(x, y), _ => true,
                w => (w.GetActualXnaPosition(), new Vector2(w.GetSize().X, w.GetSize().Y)));

        private Element TargetAt(float x, float y, string name)
        {
            Element window = WindowAt(x, y);
            return FileDropRouter.FindTarget(window, new Vector2(x, y), name, out _);
        }

        [Fact]
        public void TheWindowOnTopAtThePointIsTheOneDroppedOn()
        {
            Element sequencer = Window("sequencer", 0, 0, 2560, 1345);
            Element panel = Window("panel", 920, 258, 720, 854);

            Assert.Same(panel, WindowAt(1400, 1037));
            Assert.Same(sequencer, WindowAt(200, 600));

            sequencer.MarkBroughtForward();
            Assert.Same(sequencer, WindowAt(1400, 1037));
        }

        [Fact]
        public void APanelThatTakesNothingDoesNotPassTheDropToTheSequencerBehindIt()
        {
            Element sequencer = Window("sequencer", 0, 0, 2560, 1345);
            Register(Child(sequencer, "body", 0, 0, 2560, 1345), "sequencer", ".wav");
            Window("panel", 920, 258, 720, 854);

            Assert.Null(TargetAt(1400, 1037, "tone.wav"));
            Assert.NotNull(TargetAt(200, 600, "tone.wav"));
        }

        [Fact]
        public void TheDeepestRegistrantForTheTypeTakesIt()
        {
            Element window = Window("panel", 100, 100, 600, 400);
            Element body = Child(window, "body", 0, 0, 600, 400);
            Register(body, "body", ".wav", ".mid");
            Element slot = Child(body, "slot", 50, 50, 100, 100);
            Register(slot, "slot", ".wav");

            Assert.Same(slot, TargetAt(200, 200, "kick.WAV"));

            // The slot does not take MIDI, so the body does.
            Assert.Same(body, TargetAt(200, 200, "song.mid"));

            // Off the slot, the body.
            Assert.Same(body, TargetAt(600, 400, "kick.wav"));

            Assert.Null(TargetAt(200, 200, "notes.txt"));
        }

        [Fact]
        public void ADropOnTheWindowsChromeGoesToTheWindowsOutermostRegistrant()
        {
            Element window = Window("sequencer", 0, 0, 1000, 800);
            Element body = Child(window, "body", 0, 60, 1000, 740);
            Register(body, "body", ".wav");
            Element slot = Child(body, "slot", 10, 10, 50, 50);
            Register(slot, "slot", ".wav");

            // The title bar and menu row are the window's, not the body's.
            Assert.Same(body, TargetAt(500, 20, "tone.wav"));
            Assert.Null(TargetAt(500, 20, "song.mid"));
        }

        [Fact]
        public void AHiddenOrDetachedRegistrantIsNotHit()
        {
            Element window = Window("tabs", 0, 0, 1000, 800);
            Element sequencerTab = Child(window, "sequencer", 0, 30, 1000, 770);
            Register(sequencerTab, "sequencer", ".wav");

            // Another tab comes forward: the sequencer's content leaves the tree.
            window.Children.Remove(sequencerTab);
            Child(window, "editor", 0, 30, 1000, 770);
            Assert.Null(TargetAt(500, 400, "tone.wav"));

            Element hidden = Child(window, "hidden", 0, 30, 1000, 770);
            Register(hidden, "hidden", ".wav");
            hidden.Visible = false;
            Assert.Null(TargetAt(500, 400, "tone.wav"));
        }

        [Fact]
        public void WithoutAPointTheWindowsOwnRegistrantAnswers()
        {
            Element window = Window("panel", 100, 100, 600, 400);
            Element body = Child(window, "body", 0, 0, 600, 400);
            Register(body, "body", "*");

            Assert.Same(body, FileDropRouter.FindTarget(window, null, "anything.xyz", out IReadOnlyList<Element> chain));
            Assert.Empty(chain);
        }

        [Fact]
        public void ElementsAtListsTheChainOutermostFirst()
        {
            Element window = Window("panel", 100, 100, 600, 400);
            Element body = Child(window, "body", 10, 10, 580, 380);
            Element leaf = Child(body, "leaf", 20, 20, 50, 50);

            List<Element> chain = InputManager.ElementsAt(window, new Vector2(140, 140));
            Assert.Equal(new[] { window, body, leaf }, chain);

            Assert.Empty(InputManager.ElementsAt(window, new Vector2(50, 50)));
        }

        [Fact]
        public void ExtensionsAreMatchedWithoutCase()
        {
            Assert.Equal(".wav", TVFileDrop.ExtensionOf("C:\\a.b\\Tone.WAV"));
            Assert.Equal("", TVFileDrop.ExtensionOf("C:\\a.b\\README"));
            Assert.True(TVFileDrop.For(new[] { ".wav" }, _ => { }).Takes("X.Wav"));
            Assert.True(TVFileDrop.For(new[] { "*" }, _ => { }).Takes("x"));
        }
    }
}
