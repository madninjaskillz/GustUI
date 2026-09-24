using GustUI.Managers;

namespace GustUI.Tests
{
    /// <summary>
    /// The window the user clicked owns the keyboard (ezmuze #293), except
    /// that a click never takes it from a dialog. <see cref="InputManager.ClaimHookScope"/>
    /// is the decision; the window only supplies "is this scope another view".
    /// </summary>
    public class HookScopeClaimTests
    {
        [Fact]
        public void AClickedViewTakesTheKeyboardFromAnotherView()
        {
            var input = new InputManager();
            int sequencer = input.PushHookScope();
            int panel = input.PushHookScope();

            Assert.True(input.ClaimHookScope(sequencer, _ => true));
            Assert.Equal(sequencer, input.ActiveHookScope);

            Assert.True(input.ClaimHookScope(panel, _ => true));
            Assert.Equal(panel, input.ActiveHookScope);
        }

        [Fact]
        public void AClickNeverTakesTheKeyboardFromADialog()
        {
            var input = new InputManager();
            int sequencer = input.PushHookScope();
            int dialog = input.PushHookScope();

            Assert.False(input.ClaimHookScope(sequencer, scope => scope != dialog));
            Assert.Equal(dialog, input.ActiveHookScope);
        }

        [Fact]
        public void NoAnswerMeansTheActiveScopeKeepsTheKeyboard()
        {
            var input = new InputManager();
            int sequencer = input.PushHookScope();
            int unknown = input.PushHookScope();

            Assert.False(input.ClaimHookScope(sequencer, null));
            Assert.Equal(unknown, input.ActiveHookScope);
        }

        [Fact]
        public void AClosedScopeIsNeverRevived()
        {
            var input = new InputManager();
            int sequencer = input.PushHookScope();
            int panel = input.PushHookScope();
            input.PopHookScope(panel);

            Assert.False(input.ClaimHookScope(panel, _ => true));
            Assert.Equal(sequencer, input.ActiveHookScope);
            Assert.DoesNotContain(panel, input.HookScopes);
        }

        [Fact]
        public void AWindowWithNoScopeClaimsNothing()
        {
            var input = new InputManager();
            int panel = input.PushHookScope();

            Assert.False(input.ClaimHookScope(0, _ => true));
            Assert.Equal(panel, input.ActiveHookScope);
        }
    }
}
