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

    /// <summary>
    /// Кнопка «подключить» в окошке расширения: имя начинается с одного из слов (без учёта регистра),
    /// не содержит запрещённых слов и находится под значком расширения — так не зацепим кнопки самого сайта.
    /// </summary>
    static AutomationElement? FindConnectButton(AutomationElement win, System.Windows.Rect icon, ExtensionConfig ext)
    {
        var area = new System.Windows.Rect(icon.Right - 1000, icon.Bottom, 1100, 1000);
        var buttons = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement b in buttons)
        {
            var name = b.Current.Name.Trim();
            if (name.Length == 0 || name.Length > 40) continue;
            if (ext.NeverClick.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
            if (!ext.ConnectNames.Any(x => name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(word => word.StartsWith(x, StringComparison.OrdinalIgnoreCase)))) continue;
            var r = b.Current.BoundingRectangle;
            if (r.IsEmpty || !area.Contains(new System.Windows.Point(r.X + r.Width / 2, r.Y + r.Height / 2))) continue;
            return b;
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

            var iconRect = icon.Current.BoundingRectangle;
            ClickCenter(iconRect);

            // ждём окошко расширения
            AutomationElement? btn = null;
            for (int i = 0; i < 12 && (btn = FindConnectButton(win, iconRect, ext)) is null; i++) await Task.Delay(400);
            if (btn is not null) Log.Write($"Расширение: жму «{btn.Current.Name}»");
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
