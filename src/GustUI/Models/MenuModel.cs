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
        public string Text { get; set; }
        public String Icon { get; set; }
        public Action<ClickEventArgs> Action { get; set; }
        public List<MenuItemModel> SubItems { get; set; }
        public InputManager.KeyboardShortcut Shortcut { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
