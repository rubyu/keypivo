using System.Reflection;
using Keypivo.Core;

namespace Keypivo.UiTests;

internal static class SettingsRenderingTests
{
    public static void Run()
    {
        CheckOpaqueRepainting();
        foreach (var textScale in new[] { 1f, 1.5f, 2f })
        {
            using var font = new Font("Segoe UI", 11 * textScale);
            using var form = new SettingsForm(new() { Position = DisplayPosition.Center }, true, false,
                _ => Task.CompletedTask, new Startup()) { Font = font };
            form.Show();
            Application.DoEvents();
            var initialSize = form.Size;
            Assert(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "The settings dialog must stay inside the working area");
            foreach (var category in new[] { "General", "Launch", "Connection", "Attention", "General", "Connection" })
            {
                Find<Button>(form, "Settings" + category).PerformClick();
                form.Refresh();
                Application.DoEvents();
                Assert(form.Size == initialSize, "Switching categories must not resize the dialog");
                var page = Find<FlowLayoutPanel>(form, category + "Page");
                Program.Capture(form, $"settings-text-{textScale * 100:0}-{category.ToLowerInvariant()}");
                Assert(!page.HorizontalScroll.Visible, "Settings pages must not scroll horizontally");
                Program.CheckBounds(form);
                var rows = page.Controls.Cast<Control>().Where(control => control.Visible).OrderBy(control => control.Top).ToArray();
                for (var i = 1; i < rows.Length; i++)
                    Assert(rows[i - 1].Bottom <= rows[i].Top,
                        $"Settings rows overlap at {textScale} in {category}: {rows[i - 1].GetType().Name} '{rows[i - 1].Text}' {rows[i - 1].Bounds}, min={rows[i - 1].MinimumSize}, preferred={rows[i - 1].PreferredSize}; {rows[i].GetType().Name} '{rows[i].Text}' {rows[i].Bounds}");
                var navigation = Find<FlowLayoutPanel>(form, "SettingsNavigation");
                foreach (var button in navigation.Controls.OfType<Button>())
                {
                    var text = TextRenderer.MeasureText(button.Text, button.Font);
                    Assert(text.Width <= button.ClientSize.Width - button.Padding.Horizontal,
                        $"Navigation text must fit on one line: {button.Text} at {textScale}");
                }
                var heading = Find<Label>(form, "NotificationsHeading");
                Assert(heading.Width >= TextRenderer.MeasureText(heading.Text, heading.Font).Width,
                    "The Notifications heading must not wrap mid-word");
                if (category == "Attention")
                {
                    var last = rows.Last();
                    if (textScale == 1 && page.ClientSize.Height >= page.AutoScrollMinSize.Height)
                        Assert(last.Bottom + page.Padding.Bottom <= page.ClientSize.Height,
                            "All attention settings must fit with bottom padding when the screen has room");
                    page.ScrollControlIntoView(last);
                    Application.DoEvents();
                    Assert(last.Top >= 0 && last.Bottom <= page.ClientSize.Height,
                        "The last setting must remain reachable when text is enlarged");
                    var footer = Find<TableLayoutPanel>(form, "SettingsFooter");
                    Assert(page.PointToScreen(new Point(0, last.Bottom)).Y <= footer.PointToScreen(Point.Empty).Y,
                        "The last switch must remain above the fixed footer");
                }
            }
            Find<Button>(form, "SettingsAttention").PerformClick();
            Program.Capture(form, $"settings-text-{textScale * 100:0}");
            if (textScale == 1) Program.CaptureScreen(form, "settings-attention-screen");
            // Exercise the constrained-height path as well as a roomy desktop.
            form.ClientSize = new Size(form.ClientSize.Width, 420);
            var attention = Find<FlowLayoutPanel>(form, "AttentionPage");
            Application.DoEvents();
            Assert(attention.VerticalScroll.Visible, "A short window must expose vertical scrolling");
            attention.AutoScrollPosition = new Point(0, attention.DisplayRectangle.Height);
            Application.DoEvents();
            var lastSetting = Find<NotificationToggle>(form, "NotifySettingsCouldNotBeLoaded");
            Assert(lastSetting.Top >= 0 && lastSetting.Bottom + attention.Padding.Bottom <= attention.ClientSize.Height,
                "Scrolling to the bottom must expose the entire last setting and trailing padding");
            Assert(!attention.HorizontalScroll.Visible, "A short window must not introduce horizontal scrolling");
            Program.Capture(form, $"settings-short-{textScale * 100:0}");
        }
        Console.WriteLine("PASS Settings rendering: opaque repaint, category navigation, 100/150/200% text, and fully reachable bottom settings in short windows.");
    }

    private static void CheckOpaqueRepainting()
    {
        using var toggle = new NotificationToggle { Text = "Settings could not be loaded", BackColor = AppTheme.Surface };
        using var bitmap = new Bitmap(toggle.Width, toggle.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var paint = typeof(NotificationToggle).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var enabled in new[] { true, false })
        foreach (var selected in new[] { true, false })
        {
            toggle.Enabled = enabled;
            toggle.Checked = selected;
            graphics.Clear(Color.Magenta);
            paint.Invoke(toggle, [new PaintEventArgs(graphics, toggle.ClientRectangle)]);
            Assert(bitmap.GetPixel(4, 4).ToArgb() == toggle.BackColor.ToArgb(),
                "An opaque switch must paint its background on every repaint");
            toggle.Text = "";
            paint.Invoke(toggle, [new PaintEventArgs(graphics, toggle.ClientRectangle)]);
            var textWidth = toggle.Width - (int)(64 * toggle.DeviceDpi / 96f);
            for (var y = 0; y < toggle.Height; y++)
            for (var x = 0; x < textWidth; x++)
                Assert(bitmap.GetPixel(x, y).ToArgb() == toggle.BackColor.ToArgb(),
                    $"Repainting must erase the previous label: pixel ({x}, {y}) is {bitmap.GetPixel(x, y)}");
            toggle.Text = "Settings could not be loaded";
        }
        graphics.Clear(Color.Magenta);
        var clip = new Rectangle(20, 12, 80, 20);
        graphics.SetClip(clip);
        paint.Invoke(toggle, [new PaintEventArgs(graphics, clip)]);
        for (var y = 0; y < toggle.Height; y++)
        for (var x = 0; x < toggle.Width; x++)
            if (!clip.Contains(x, y))
                Assert(bitmap.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb(), "Partial repaint must preserve the graphics clip");
    }

    private sealed class Startup : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync() => Task.FromResult(StartupStatus.Enabled);
        public Task<StartupStatus> SetEnabledAsync(bool enabled) => Task.FromResult(enabled ? StartupStatus.Enabled : StartupStatus.Disabled);
    }

    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
