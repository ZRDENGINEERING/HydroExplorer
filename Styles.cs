using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace HydroExplorer
{
    public static class Styles
    {
        public static ComponentResourceKey AccentButton => new(typeof(Styles), "AccentButton");

        public static ComponentResourceKey ToolbarButton => new(typeof(Styles), "ToolbarButton");

        public static ComponentResourceKey AccentToolbarButton => new(typeof(Styles), "AccentToolbarButton");

        public static ComponentResourceKey ToolbarToggleButton => new(typeof(Styles), "ToolbarToggleButton");

        public static ComponentResourceKey DefaultToAccentToggleButton => new(typeof(Styles), "DefaultToAccentToggleButton");

        public static ComponentResourceKey AccentComboBox => new(typeof(Styles), "AccentComboBox");

        public static ComponentResourceKey AccentComboBoxItem => new(typeof(Styles), "AccentComboBoxItem");

        public static ComponentResourceKey WindowButton => new(typeof(Styles), "WindowButton");

        public static ComponentResourceKey WindowCloseButton => new(typeof(Styles), "WindowCloseButton");

        public static ComponentResourceKey WindowToggleButton => new(typeof(Styles), "WindowToggleButton");

        public static ComponentResourceKey RippleListBoxItem => new(typeof(Styles), "RippleListBoxItem");

        public static ComponentResourceKey SelectableTextBlockTextBox => new(typeof(Styles), "SelectableTextBlockTextBox");

        public static ComponentResourceKey ToggleSwitch => new(typeof(Styles), "ToggleSwitch");
    }
}