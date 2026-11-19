global using Button = AntdUI.Button;
global using CheckBox = AntdUI.Checkbox;

namespace KartyLeczenia.Windows;

internal sealed class ThemeSelector : Button
{
    internal ThemeSelector()
    {
        AutoSize = true;
        TabStop = true;
        WaveSize = 0;
        AccessibleRole = AccessibleRole.RadioButton;
    }
}
