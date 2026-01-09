using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RelayTest.Properties;

public class SettingsForm : Form
{
    private readonly TextBox txtJournalPath;
    private readonly Button btnBrowse;
    private readonly TextBox txtRelayAddress;
    private readonly TextBox txtAuthToken;
    private readonly ComboBox cmbMode;
    private readonly TextBox[] txtRelayEventName = new TextBox[4];
    private readonly NumericUpDown[] nudRelayDuration = new NumericUpDown[4];
    private readonly TextBox[] txtHotkeys = new TextBox[6];
    private readonly NumericUpDown nudCooldownSeconds;
    private readonly Button btnSave;
    private readonly Button btnCancel;

    public SettingsForm()
    {
        // Restore saved geometry if present
        if (Settings.Default.SettingsFormWidth > 0 && Settings.Default.SettingsFormHeight > 0)
        {
            // ensure minimums are respected
            var minW = 640;
            var minH = 420;
            var w = Math.Max(minW, Settings.Default.SettingsFormWidth);
            var h = Math.Max(minH, Settings.Default.SettingsFormHeight);
            Size = new Size(w, h);
            StartPosition = FormStartPosition.Manual;
        }
        else
        {
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 420);
        }

        // If saved location is valid, apply it (do not force if off-screen)
        if (Settings.Default.SettingsFormLeft != int.MinValue && Settings.Default.SettingsFormTop != int.MinValue)
        {
            try
            {
                var pt = new Point(Settings.Default.SettingsFormLeft, Settings.Default.SettingsFormTop);
                Location = pt;
                StartPosition = FormStartPosition.Manual;
            }
            catch { /* ignore invalid saved location */ }
        }

        // Restore WindowState (Maximized/Normal)
        if (string.Equals(Settings.Default.SettingsFormWindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
            WindowState = FormWindowState.Maximized;

        // Basic initialization that existed previously
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        MinimizeBox = false;
        MaximizeBox = false;
        Padding = new Padding(8);

        // Main two-column layout: label | control (control column expands)
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // helper to create wrapped labels
        Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                MaximumSize = new Size(520, 0) // wrap if long
            };
        }

        int row = 0;

        // Journal path row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("Journal folder:"), 0, row);
        txtJournalPath = new TextBox { Text = Settings.Default.JournalPath ?? string.Empty, Dock = DockStyle.Fill };
        layout.Controls.Add(txtJournalPath, 1, row);
        row++;

        // Browse button on separate row (right-aligned under control)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var browsePanel = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true };
        btnBrowse = new Button { Text = "Browse...", AutoSize = true, Anchor = AnchorStyles.Right };
        btnBrowse.Click += BtnBrowse_Click;
        browsePanel.Controls.Add(btnBrowse);
        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, row);
        layout.Controls.Add(browsePanel, 1, row);
        row++;

        // Relay address
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("Relay host (host:port or http://host:port):"), 0, row);
        txtRelayAddress = new TextBox { Text = Settings.Default.RelayAddress ?? string.Empty, Dock = DockStyle.Fill };
        layout.Controls.Add(txtRelayAddress, 1, row);
        row++;

        // Auth token
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("Auth token (optional):"), 0, row);
        txtAuthToken = new TextBox { Text = Settings.Default.AuthToken ?? string.Empty, Dock = DockStyle.Fill };
        layout.Controls.Add(txtAuthToken, 1, row);
        row++;

        // App mode
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("App mode:"), 0, row);
        cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 220 };
        cmbMode.Items.AddRange(new object[] { "Stand Alone", "Relay", "Game" });
        cmbMode.SelectedItem = Settings.Default.AppMode switch
        {
            "Relay" => "Relay",
            "Game" => "Game",
            _ => "Stand Alone"
        };
        layout.Controls.Add(cmbMode, 1, row);
        row++;

        // Activation cooldown (seconds)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("Activation cooldown (seconds):"), 0, row);
        nudCooldownSeconds = new NumericUpDown { Minimum = 1, Maximum = 60, Value = Math.Clamp(Settings.Default.ActivationCooldownMs / 1000, 1, 60), Width = 90 };
        layout.Controls.Add(nudCooldownSeconds, 1, row);
        row++;

        // Hotkey configuration section
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("Hotkey Configuration (e.g., Shift+D1, Ctrl+Alt+D2):"), 0, row);
        row++;

        var hotkeyTable = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        hotkeyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hotkeyConfigs = new (string label, string settingName, string defaultValue)[]
        {
            ("Relay 1 Short Fog:", nameof(Settings.Default.HotkeyRelay1Short), Settings.Default.HotkeyRelay1Short),
            ("Relay 1 Long Fog:", nameof(Settings.Default.HotkeyRelay1Long), Settings.Default.HotkeyRelay1Long),
            ("Relay 2 Toggle:", nameof(Settings.Default.HotkeyRelay2Toggle), Settings.Default.HotkeyRelay2Toggle),
            ("Relay 3 Toggle:", nameof(Settings.Default.HotkeyRelay3Toggle), Settings.Default.HotkeyRelay3Toggle),
            ("Relay 4 Toggle:", nameof(Settings.Default.HotkeyRelay4Toggle), Settings.Default.HotkeyRelay4Toggle),
            ("Start/Stop:", nameof(Settings.Default.HotkeyStartStop), Settings.Default.HotkeyStartStop),
        };

        int hotkeyRow = 0;
        foreach (var (label, settingName, defaultValue) in hotkeyConfigs)
        {
            hotkeyTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            hotkeyTable.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, hotkeyRow);
            var tb = new TextBox { Text = defaultValue ?? string.Empty, Dock = DockStyle.Fill };
            txtHotkeys[hotkeyRow] = tb;
            hotkeyTable.Controls.Add(tb, 1, hotkeyRow);
            hotkeyRow++;
        }

        layout.Controls.Add(hotkeyTable, 1, row);
        row++;

        // Per-relay event name + duration – one pair per relay
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(MakeLabel("Per-relay event name and activation duration (seconds):"), 0, row);

        var relayTable = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // label "Relay 1:"
        relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f)); // event name textbox
        relayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f)); // duration numeric

        // Add header row for clarity (empty first column, then headers)
        relayTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        relayTable.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 0);
        relayTable.Controls.Add(new Label { Text = "Event name", AutoSize = true }, 1, 0);
        relayTable.Controls.Add(new Label { Text = "Duration (s)", AutoSize = true }, 2, 0);

        var currentEvents = Settings.Default.RelayEventTypes;
        var currentDurations = Settings.Default.RelayActivateMs;
        for (int i = 0; i < 4; i++)
        {
            relayTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { Text = $"Relay {i + 1}:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 6, 0, 0) };

            var eventTb = new TextBox { Text = (currentEvents != null && i < currentEvents.Length) ? currentEvents[i] ?? string.Empty : string.Empty, Dock = DockStyle.Fill };
            txtRelayEventName[i] = eventTb;

            var dur = (currentDurations != null && i < currentDurations.Length) ? Math.Clamp(currentDurations[i] / 1000, 1, 600) : 3;
            var nud = new NumericUpDown { Minimum = 1, Maximum = 600, Value = dur, Width = 90 }; // seconds
            nudRelayDuration[i] = nud;

            // Place controls on row (row index = i + 1 because header is row 0)
            relayTable.Controls.Add(lbl, 0, i + 1);
            relayTable.Controls.Add(eventTb, 1, i + 1);
            relayTable.Controls.Add(nud, 2, i + 1);
        }

        layout.Controls.Add(relayTable, 1, row);
        row++;

        // Helpful info / hint row (multi-line)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new Label
        {
            Text = "Notes:\n- Enter event names separated by commas for each relay (e.g. HeatWarning, ShieldDown).\n- Set per-relay activation durations (seconds) and the shared activation cooldown.\n- Settings are saved to settings.json in the application folder.\n- If installed under Program Files, saving settings may require elevated rights.",
            AutoSize = true,
            MaximumSize = new Size(920, 0),
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        layout.Controls.Add(hint, 1, row);
        row++;

        // Buttons row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        btnSave.Click += BtnSave_Click;
        btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);
        layout.Controls.Add(btnPanel, 1, row);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        Controls.Add(layout);

        // Always persist geometry when the dialog is closed so user doesn't need to resize each time.
        FormClosing += SettingsForm_FormClosing;
    }

    private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveWindowBounds();
    }

    private void SaveWindowBounds()
    {
        try
        {
            // When maximized, use RestoreBounds to capture the normal size/location
            if (WindowState == FormWindowState.Maximized)
            {
                var r = RestoreBounds;
                Settings.Default.SettingsFormWidth = r.Width;
                Settings.Default.SettingsFormHeight = r.Height;
                Settings.Default.SettingsFormLeft = r.Left;
                Settings.Default.SettingsFormTop = r.Top;
                Settings.Default.SettingsFormWindowState = "Maximized";
            }
            else if (WindowState == FormWindowState.Minimized)
            {
                // do not store minimized state as the preferred startup state; keep size/location
                var r = RestoreBounds;
                Settings.Default.SettingsFormWidth = r.Width;
                Settings.Default.SettingsFormHeight = r.Height;
                Settings.Default.SettingsFormLeft = r.Left;
                Settings.Default.SettingsFormTop = r.Top;
                Settings.Default.SettingsFormWindowState = "Normal";
            }
            else
            {
                Settings.Default.SettingsFormWidth = Width;
                Settings.Default.SettingsFormHeight = Height;
                Settings.Default.SettingsFormLeft = Location.X;
                Settings.Default.SettingsFormTop = Location.Y;
                Settings.Default.SettingsFormWindowState = "Normal";
            }

            Settings.Default.Save();
        }
        catch
        {
            // non-fatal; ignore failures saving geometry
        }
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            SelectedPath = txtJournalPath.Text,
            Description = "Select Elite Dangerous folder containing journal files"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtJournalPath.Text = dlg.SelectedPath;
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        // Persist to Settings.Default
        Settings.Default.JournalPath = txtJournalPath.Text?.Trim() ?? string.Empty;
        Settings.Default.RelayAddress = txtRelayAddress.Text?.Trim() ?? string.Empty;
        Settings.Default.AuthToken = txtAuthToken.Text?.Trim() ?? string.Empty;
        Settings.Default.AppMode = (cmbMode.SelectedItem as string) switch
        {
            "Relay" => "Relay",
            "Game" => "Game",
            _ => "StandAlone"
        };

        // Save shared activation cooldown (seconds -> ms)
        Settings.Default.ActivationCooldownMs = (int)nudCooldownSeconds.Value * 1000;

        // Save hotkey configurations
        Settings.Default.HotkeyRelay1Short = txtHotkeys[0].Text?.Trim() ?? Settings.Default.HotkeyRelay1Short;
        Settings.Default.HotkeyRelay1Long = txtHotkeys[1].Text?.Trim() ?? Settings.Default.HotkeyRelay1Long;
        Settings.Default.HotkeyRelay2Toggle = txtHotkeys[2].Text?.Trim() ?? Settings.Default.HotkeyRelay2Toggle;
        Settings.Default.HotkeyRelay3Toggle = txtHotkeys[3].Text?.Trim() ?? Settings.Default.HotkeyRelay3Toggle;
        Settings.Default.HotkeyRelay4Toggle = txtHotkeys[4].Text?.Trim() ?? Settings.Default.HotkeyRelay4Toggle;
        Settings.Default.HotkeyStartStop = txtHotkeys[5].Text?.Trim() ?? Settings.Default.HotkeyStartStop;

        // Save relay event names and durations (manual entry)
        var events = new string[4];
        var durations = new int[4];
        for (int i = 0; i < 4; i++)
        {
            events[i] = txtRelayEventName[i].Text?.Trim() ?? string.Empty;
            durations[i] = (int)nudRelayDuration[i].Value * 1000;
        }
        Settings.Default.RelayEventTypes = events;
        Settings.Default.RelayActivateMs = durations;

        // Also persist current window bounds so the dialog opens at the same size next time
        SaveWindowBounds();

        Settings.Default.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}