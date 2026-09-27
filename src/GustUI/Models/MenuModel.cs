using GustUI.Managers;
using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Models
{
    public class MenuItemModel
    {
        private string text;
        private bool enabled = true;

        /// <summary>The row's label. When <see cref="TextWhen"/> is set, that
        /// is asked instead, every time the label is read.</summary>
        public string Text
        {
            get => TextWhen != null ? TextWhen() ?? "" : text;
            set => text = value;
        }

        public String Icon { get; set; }
        public Action<ClickEventArgs> Action { get; set; }
        public List<MenuItemModel> SubItems { get; set; }
        public InputManager.KeyboardShortcut Shortcut { get; set; }

        /// <summary>Whether the row can be pressed. When
        /// <see cref="EnabledWhen"/> is set, that is asked instead, every time
        /// this is read, and whatever was assigned here is ignored.</summary>
        public bool Enabled
        {
            get => EnabledWhen != null ? EnabledWhen() : enabled;
            set => enabled = value;
        }

        /// <summary>
        /// A LIVE enabled state (ezmuze #411). A menu-bar menu is built once,
        /// with its view, and lives as long as the view does, so a fixed
        /// <see cref="Enabled"/> says what was possible when the view opened,
        /// not what is possible now: Edit > Copy stayed grey after a
        /// selection was made. A predicate here is asked each time the
        /// question matters: when a menu showing the row opens (a row is built
        /// on open and keeps the answer while it is up), and when the menu bar
        /// looks for an item to run for a key. Keep it cheap and side-effect
        /// free; it is also asked by the help screen's shortcut list.
        /// </summary>
        public Func<bool> EnabledWhen { get; set; }

        /// <summary>A live label, the partner of <see cref="EnabledWhen"/>: a
        /// row that says why it is disabled ("Glue (select two or more
        /// clips)") or how much it will do ("... (3 clips)") says it about
        /// now. Asked when the menu opens and sizes itself.</summary>
        public Func<string> TextWhen { get; set; }
    }
}
