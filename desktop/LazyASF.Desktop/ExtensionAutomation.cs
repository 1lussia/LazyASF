using System.Windows.Automation;
using LazyASF.Native;

namespace LazyASF;

/// <summary>
/// Включение VPN-расширения (ЮБуст) через UI Automation: Chrome отдаёт Windows дерево своих кнопок,
/// поэтому иконку на панели и кнопку «Verbinden» в окошке можно найти по названию — без координат,
/// на любом экране и при любом масштабе.
/// </summary>
public static class ExtensionAutomation
{
    static void ClickCenter(System.Windows.Rect r)
    {
        Input.MoveTo((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
        Thread.Sleep(120);
        Input.Click();
    }

    static AutomationElement? FindToolbarButton(AutomationElement win, string name)
    {
        var all = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "ToolbarActionView"));
        foreach (AutomationElement b in all)
            if (b.Current.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) return b;
        return null;
    }

    static AutomationElement? FindButton(AutomationElement win, IEnumerable<string> names)
    {
        foreach (var n in names)
        {
            var e = win.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, n),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
            if (e is not null) return e;
        }
        return null;
    }

    /// <summary>true — расширение подключено (или уже было подключено).</summary>
    public static async Task<(bool ok, string detail)> Connect(IntPtr chromeWindow, ExtensionConfig ext)
    {
        try
        {
            if (!Win.Activate(chromeWindow)) Log.Write("Chrome не на переднем плане — пробую всё равно");
            var win = AutomationElement.FromHandle(chromeWindow);

            AutomationElement? icon = null;
            for (int i = 0; i < 20 && (icon = FindToolbarButton(win, ext.ToolbarName)) is null; i++) await Task.Delay(300);
            if (icon is null) return (false, $"Нет значка «{ext.ToolbarName}» на панели Chrome — закрепи его (пазл → 📌)");

            ClickCenter(icon.Current.BoundingRectangle);

            // ждём окошко расширения
            AutomationElement? btn = null;
            for (int i = 0; i < 20 && (btn = FindButton(win, ext.ConnectNames)) is null; i++) await Task.Delay(300);
            if (btn is null)
            {
                // кнопки «подключить» нет — скорее всего, уже подключено
                Input.Combo("esc");
                return (true, "уже было включено");
            }
            ClickCenter(btn.Current.BoundingRectangle);
            await Task.Delay(2500);
            Input.Combo("esc");
            return (true, "подключено");
        }
        catch (Exception e)
        {
            Log.Write($"Расширение: {e.Message}");
            return (false, e.Message);
        }
    }
}
