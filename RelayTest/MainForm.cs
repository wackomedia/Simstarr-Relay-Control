using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using RelayTest.Properties;
#if EMBED_HTTP_SERVER
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
#endif

public class MainForm : Form
{
    private readonly TextBox txtJournalPath;
    private readonly Button btnBrowse;
    private readonly Button btnStartStop;
    private readonly TextBox txtLog;
    private readonly Button btnFog3;
    private readonly Button btnFog5;
    private readonly Button btnR2;
    private readonly Button btnR3;
    private readonly Button btnR4;
    private readonly FlowLayoutPanel pnlMode;
    private readonly ComboBox cmbMode;
    private readonly TextBox txtRelayAddress;
    private readonly TextBox txtAuthToken;
    private readonly Label lblRelayDisplay;

    private enum AppMode { Relay, Game, StandAlone }
    private AppMode _mode = AppMode.StandAlone;

    private JournalWatcher? _watcher;
    private RelayController? _relays;
    private EventForwarder? _forwarder;
    private bool _running = false;

    private Action? _onHeatWarning;
    private Action? _onHeatDamage;
    private Action<string>? _onDebugLine;

    private readonly object _activationLock = new object();
    private readonly TimeSpan ActivationCooldown = TimeSpan.FromSeconds(5);
    private DateTime _lastActivationUtc = DateTime.MinValue;

    private const int FogRelayIndex = 0;
    private readonly bool[] _relayStates = new bool[4];

    private readonly Color EdBackground = Color.FromArgb(10, 10, 12);
    private readonly Color EdPanel = Color.FromArgb(20, 20, 24);
    private readonly Color EdOrange = Color.FromArgb(255, 140, 0);
    private readonly Color EdText = Color.FromArgb(230, 230, 230);
    private readonly Color EdBlue = Color.FromArgb(0, 174, 239);

    private CancellationTokenSource? _fogActiveCts;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    private const int HOT_R1_3 = 1;
    private const int HOT_R1_5 = 2;
    private const int HOT_R2_TOGGLE = 3;
    private const int HOT_R3_TOGGLE = 4;
    private const int HOT_R4_TOGGLE = 5;
    private const int HOT_STARTSTOP = 6;

    public MainForm()
    {
        Text = "Simstarr Relay Control";
        Width = 900;
        Height = 600;
        MinimumSize = new Size(700, 420);

        BackColor = EdBackground;
        ForeColor = EdText;

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 9,
            RowCount = 2,
            Padding = new Padding(8),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            BackColor = EdPanel
        };

        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 7; i++)
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        pnlMode = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            BackColor = EdPanel,
            Margin = new Padding(0, 4, 8, 4)
        };

        cmbMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 160,
            BackColor = EdPanel,
            ForeColor = EdText,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 2, 0, 2)
        };
        cmbMode.Items.AddRange(new object[] { "Stand Alone", "Relay PC", "Game PC" });
        cmbMode.SelectedIndex = 0;
        cmbMode.SelectedIndexChanged += (s, e) =>
        {
            switch (cmbMode.SelectedIndex)
            {
                case 0: SetMode(AppMode.StandAlone); break;
                case 1: SetMode(AppMode.Relay); break;
                case 2: SetMode(AppMode.Game); break;
            }
        };

        var lblRelayHint = new Label
        {
            Text = "Relay host (host:port or http://host:port):",
            AutoSize = true,
            ForeColor = EdText,
            BackColor = EdPanel,
            Margin = new Padding(0, 6, 0, 0)
        };

        txtRelayAddress = new TextBox
        {
            Text = string.Empty,
            Width = 160,
            BackColor = EdPanel,
            ForeColor = EdText,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 2)
        };

        txtAuthToken = new TextBox
        {
            Text = string.Empty,
            Width = 160,
            BackColor = EdPanel,
            ForeColor = EdText,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 0, 2)
        };

        lblRelayDisplay = new Label
        {
            Text = "Relay Endpoint: (none)",
            AutoSize = true,
            ForeColor = EdBlue,
            BackColor = EdPanel,
            Margin = new Padding(0, 4, 0, 0)
        };

        pnlMode.Controls.Add(cmbMode);
        pnlMode.Controls.Add(lblRelayHint);
        pnlMode.Controls.Add(txtRelayAddress);
        pnlMode.Controls.Add(txtAuthToken);
        pnlMode.Controls.Add(lblRelayDisplay);

        var authTip = new ToolTip { IsBalloon = false, ShowAlways = true };
        authTip.SetToolTip(txtAuthToken, "Optional auth token sent as 'X-Auth-Token' header to the relay server. Leave empty if the relay does not require authentication.");

        txtJournalPath = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 6),
            BackColor = EdPanel,
            ForeColor = EdText,
            BorderStyle = BorderStyle.FixedSingle
        };

        btnBrowse = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(6, 4, 6, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnBrowse.FlatAppearance.BorderColor = EdOrange;

        btnStartStop = new Button
        {
            Text = "Start",
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(8, 4, 8, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnStartStop.FlatAppearance.BorderColor = EdOrange;

        btnFog3 = new Button
        {
            Text = "R1 Fog 3s",
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(6, 4, 6, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnFog3.FlatAppearance.BorderColor = EdOrange;

        btnFog5 = new Button
        {
            Text = "R1 Fog 5s",
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(6, 4, 6, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnFog5.FlatAppearance.BorderColor = EdOrange;

        btnR2 = new Button
        {
            Text = "R2: OFF",
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(6, 4, 6, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnR2.FlatAppearance.BorderColor = EdOrange;

        btnR3 = new Button
        {
            Text = "R3: OFF",
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(6, 4, 6, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnR3.FlatAppearance.BorderColor = EdOrange;

        btnR4 = new Button
        {
            Text = "R4: OFF",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(6, 4, 6, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnR4.FlatAppearance.BorderColor = EdOrange;

        topLayout.Controls.Add(pnlMode, 0, 0);
        topLayout.SetRowSpan(pnlMode, 2);
        topLayout.Controls.Add(txtJournalPath, 1, 0);
        topLayout.SetRowSpan(txtJournalPath, 2);
        topLayout.Controls.Add(btnBrowse, 2, 0);
        topLayout.Controls.Add(btnStartStop, 3, 0);
        topLayout.Controls.Add(btnFog3, 4, 0);
        topLayout.Controls.Add(btnFog5, 5, 0);
        topLayout.Controls.Add(btnR2, 2, 1);
        topLayout.Controls.Add(btnR3, 3, 1);
        topLayout.Controls.Add(btnR4, 4, 1);

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            BackColor = EdBackground,
            ForeColor = EdOrange,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(txtLog);
        Controls.Add(topLayout);

        // Load saved values
        txtJournalPath.Text = Settings.Default.JournalPath ?? string.Empty;
        txtRelayAddress.Text = Settings.Default.RelayAddress ?? string.Empty;

        _mode = Settings.Default.AppMode switch
        {
            "Relay" => AppMode.Relay,
            "Game" => AppMode.Game,
            _ => AppMode.StandAlone
        };
        cmbMode.SelectedIndex = _mode switch
        {
            AppMode.StandAlone => 0,
            AppMode.Relay => 1,
            AppMode.Game => 2,
            _ => 0
        };

        SetMode(_mode); // also updates relay display

        btnBrowse.Click += BtnBrowse_Click;
        btnStartStop.Click += BtnStartStop_Click;
        btnFog3.Click += (s, e) => OnManualFogClicked(FogRelayIndex, 3000);
        btnFog5.Click += (s, e) => OnManualFogClicked(FogRelayIndex, 5000);
        btnR2.Click += (s, e) => ToggleRelay(1);
        btnR3.Click += (s, e) => ToggleRelay(2);
        btnR4.Click += (s, e) => ToggleRelay(3);

        txtRelayAddress.Leave += (s, e) =>
        {
            var raw = txtRelayAddress.Text.Trim();
            var norm = NormalizeRelayAddress(raw);
            // If user omitted http, we add it and show the updated value.
            if (!string.Equals(raw, norm, StringComparison.Ordinal))
                txtRelayAddress.Text = norm;

            if (Settings.Default.RelayAddress != norm)
            {
                Settings.Default.RelayAddress = norm;
                Settings.Default.Save();
                AppendLog("Relay address saved (normalized).");
            }
            UpdateRelayDisplay();
        };

        FormClosing += MainForm_FormClosing;

        pnlMode.TabIndex = 0;
        cmbMode.TabIndex = 0;
        txtRelayAddress.TabIndex = 1;
        txtAuthToken.TabIndex = 2;
        btnBrowse.TabIndex = 3;
        txtJournalPath.TabIndex = 4;
        btnStartStop.TabIndex = 5;
        btnFog3.TabIndex = 6;
        btnFog5.TabIndex = 7;
        btnR2.TabIndex = 8;
        btnR3.TabIndex = 9;
        btnR4.TabIndex = 10;

        SetManualButtonsEnabled(false);
    }

    private void SetMode(AppMode mode)
    {
        _mode = mode;
        Settings.Default.AppMode = mode switch
        {
            AppMode.Relay => "Relay",
            AppMode.Game => "Game",
            _ => "StandAlone"
        };
        Settings.Default.Save();

        AppendLog(mode switch
        {
            AppMode.Relay => "Mode: Relay PC",
            AppMode.Game => "Mode: Game PC",
            AppMode.StandAlone => "Mode: Stand Alone",
            _ => "Mode changed"
        });
        cmbMode.Enabled = !(_running);
        SetManualButtonsEnabled(_running && (_mode == AppMode.Relay || _mode == AppMode.StandAlone));
        txtJournalPath.Enabled = _mode != AppMode.Relay;
        btnBrowse.Enabled = _mode != AppMode.Relay;
        UpdateRelayDisplay();
    }

    private string NormalizeRelayAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var a = raw.Trim();
        if (!a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            a = "http://" + a;
        return a.TrimEnd('/');
    }

    private static string[] GetLocalIpv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(i => i.AddressFamily == AddressFamily.InterNetwork)
                .Select(i => i.ToString())
                .Distinct()
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string? GetPrimaryIpv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(i => i.AddressFamily == AddressFamily.InterNetwork)
                .Select(i => i.ToString())
                .Where(ip =>
                    !ip.StartsWith("127.") &&              // skip loopback
                    !ip.StartsWith("169.254.") &&          // skip APIPA
                    ip != "0.0.0.0")
                .OrderBy(ip =>
                {
                    // Prefer typical LAN ranges
                    if (ip.StartsWith("192.168.")) return 0;
                    if (ip.StartsWith("10.")) return 1;
                    if (ip.StartsWith("172.")) return 2;
                    return 3;
                })
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private void UpdateRelayDisplay()
    {
        if (_mode == AppMode.Relay)
        {
#if EMBED_HTTP_SERVER
            var primary = GetPrimaryIpv4();
            var ips = GetLocalIpv4();
            var list = ips.Length == 0 ? "(no IPv4)" : string.Join(", ", ips);
            if (primary != null)
            {
                // Show EXACT string the Game PC should type (no http) plus full URL variant.
                lblRelayDisplay.Text =
                    $"Relay Mode: Listening on port 5000\n" +
                    $"Game PC enter: {primary}:5000\n" +
                    $"(Full URL if needed: http://{primary}:5000)\n" +
                    $"All local IPs: {list}";
            }
            else
            {
                lblRelayDisplay.Text =
                    $"Relay Mode: Listening on port 5000\n" +
                    $"Game PC: (no usable IPv4 detected)\n" +
                    $"All local IPs: {list}";
            }
#else
            lblRelayDisplay.Text = "Relay Mode: Hardware active.";
#endif
            return;
        }

        var norm = NormalizeRelayAddress(txtRelayAddress.Text);
        // Write normalized version back so user can copy exact value (removes trailing slash, adds http://).
        if (!string.Equals(txtRelayAddress.Text, norm, StringComparison.Ordinal))
            txtRelayAddress.Text = norm;

        // Display both forms: plain host:port (without protocol) and full URL
        if (string.IsNullOrEmpty(norm))
        {
            lblRelayDisplay.Text = "Relay Endpoint: (none)";
        }
        else
        {
            var plain = norm.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? norm.Substring("http://".Length)
                : (norm.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? norm.Substring("https://".Length) : norm);

            lblRelayDisplay.Text =
                $"Relay Endpoint: {norm}\n" +
                $"Game PC can enter: {plain}";
        }
    }

    private bool IsLocalRelayMode() => _mode == AppMode.Relay || _mode == AppMode.StandAlone;

    private void SetManualButtonsEnabled(bool enabled)
    {
        btnFog3.Enabled = enabled;
        btnFog5.Enabled = enabled;
        btnR2.Enabled = enabled;
        btnR3.Enabled = enabled;
        btnR4.Enabled = enabled;
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
            Settings.Default.JournalPath = dlg.SelectedPath;
            Settings.Default.Save();
            AppendLog("Journal path saved.");
        }
    }

    private void SetFogActive(int durationMs)
    {
        _fogActiveCts?.Cancel();
        _fogActiveCts?.Dispose();
        _fogActiveCts = new CancellationTokenSource();
        var ct = _fogActiveCts.Token;

        void SetOn()
        {
            btnFog3.BackColor = EdOrange;
            btnFog3.ForeColor = EdText;
            btnFog3.FlatAppearance.BorderColor = Color.FromArgb(200, 110, 0);
            btnFog5.BackColor = EdOrange;
            btnFog5.ForeColor = EdText;
            btnFog5.FlatAppearance.BorderColor = Color.FromArgb(200, 110, 0);
        }
        void SetOff()
        {
            btnFog3.BackColor = EdPanel;
            btnFog3.ForeColor = EdOrange;
            btnFog3.FlatAppearance.BorderColor = EdOrange;
            btnFog5.BackColor = EdPanel;
            btnFog5.ForeColor = EdOrange;
            btnFog5.FlatAppearance.BorderColor = EdOrange;
        }
        if (InvokeRequired) BeginInvoke((Action)SetOn); else SetOn();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(durationMs, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            if (!IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)SetOff); else SetOff();
        });
    }

    private async void OnManualFogClicked(int relayIndex, int durationMs)
    {
        lock (_activationLock)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastActivationUtc;
            if (elapsed < ActivationCooldown)
            {
                var remaining = ActivationCooldown - elapsed;
                AppendLog($"Fog ignored: on cooldown ({remaining.TotalSeconds:F1}s remaining)");
                return;
            }
            _lastActivationUtc = now;
        }

        if (IsLocalRelayMode())
        {
            if (_relays == null) { AppendLog("Relay not initialized."); return; }
            SetFogActive(durationMs);
            AppendLog($"Manual: Relay {relayIndex + 1} Fog {durationMs / 1000}s");
            return;
        }

        if (!_running) { AppendLog("Not running."); return; }
        EnsureForwarderCreated();
        if (_forwarder == null) { AppendLog("Forwarder not available."); return; }

        var ok = await _forwarder.SendFogAsync(relayIndex, durationMs).ConfigureAwait(false);
        if (ok)
        {
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() => SetFogActive(durationMs)));
                else SetFogActive(durationMs);
            }
            AppendLog($"Forwarded: Fog relay {relayIndex + 1} {durationMs / 1000}s");
        }
        else AppendLog("Forward failed: Fog");
    }

    private async void ToggleRelay(int relayIndex)
    {
        if (IsLocalRelayMode())
        {
            if (_relays == null) { AppendLog("Relay not initialized."); return; }
            bool newState;
            lock (_activationLock) { newState = !_relayStates[relayIndex]; _relayStates[relayIndex] = newState; }
            try
            {
                _relays.SetRelay(relayIndex, newState);
                UpdateRelayButtonVisual(relayIndex, newState);
                AppendLog($"Relay {relayIndex + 1} turned {(newState ? "ON" : "OFF")} (local)");
            }
            catch (Exception ex) { AppendLog($"Failed to toggle relay {relayIndex + 1}: {ex.Message}"); }
            return;
        }

        if (!_running) { AppendLog("Not running."); return; }
        EnsureForwarderCreated();
        if (_forwarder == null) { AppendLog("Forwarder not available."); return; }

        bool desiredState = !_relayStates[relayIndex];
        var ok = await _forwarder.SendSetRelayAsync(relayIndex, desiredState).ConfigureAwait(false);
        if (ok)
        {
            lock (_activationLock) { _relayStates[relayIndex] = desiredState; }
            UpdateRelayButtonVisual(relayIndex, desiredState);
            AppendLog($"Forwarded: SetRelay {relayIndex + 1} -> {(desiredState ? "ON" : "OFF")}" +
                $" (relay={_relayStates[relayIndex]})");
        }
        else AppendLog($"Forward failed: SetRelay {relayIndex + 1}");
    }

    private void EnsureForwarderCreated()
    {
        if (_forwarder != null) return;
        try
        {
            var addr = txtRelayAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) { UpdateRelayDisplay(); return; }
            addr = NormalizeRelayAddress(addr);
            // Update textbox so user sees the exact normalized form (optional protocol auto-added).
            if (!string.Equals(txtRelayAddress.Text, addr, StringComparison.Ordinal))
                txtRelayAddress.Text = addr;

            _forwarder = new EventForwarder(addr, txtAuthToken.Text ?? string.Empty);
            AppendLog($"Forwarder created for {addr}");
            UpdateRelayDisplay();
            _ = Task.Run(async () =>
            {
                try
                {
                    var ok = await _forwarder.PingAsync().ConfigureAwait(false);
                    AppendLog(ok ? $"Relay reachable at {addr}" : $"Relay not reachable at {addr}");
                }
                catch (Exception ex) { AppendLog($"Relay ping failed: {ex.Message}"); }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to create forwarder: {ex.Message}");
            _forwarder = null;
            UpdateRelayDisplay();
        }
    }

    private async void BtnStartStop_Click(object? sender, EventArgs e)
    {
        if (!_running)
        {
            // Persist relay address when starting
            var currentRelay = txtRelayAddress.Text.Trim();
            if (Settings.Default.RelayAddress != currentRelay)
            {
                Settings.Default.RelayAddress = currentRelay;
                Settings.Default.Save();
                AppendLog("Relay address saved (on start).");
            }

            if (_mode == AppMode.Relay)
            {
                // Relay hardware only: DO NOT create or use JournalWatcher.
                _forwarder?.Dispose(); _forwarder = null;
                _relays = new RelayController();
                AppendLog("Initializing relay (hardware only)...");
                if (!_relays.Init())
                {
                    AppendLog("Relay init failed.");
                    _relays.Dispose();
                    _relays = null;
                    return;
                }

                for (int i = 0; i < 4; i++) _relayStates[i] = false;
                UpdateRelayButtonVisual(1, false); UpdateRelayButtonVisual(2, false); UpdateRelayButtonVisual(3, false);

                _running = true;
                btnStartStop.Text = "Stop";
                SetManualButtonsEnabled(true);

                RegisterHotKeys();
                cmbMode.Enabled = false;
                AppendLog("Relay hardware initialized (Relay PC). No journal monitoring.");
#if EMBED_HTTP_SERVER
                StartEmbeddedServer(5000);
#endif
            }
            else if (_mode == AppMode.StandAlone)
            {
                // Stand Alone: local hardware + journal monitoring
                _forwarder?.Dispose(); _forwarder = null;
                _relays = new RelayController();
                AppendLog("Initializing relay...");
                if (!_relays.Init()) { AppendLog("Relay init failed."); _relays.Dispose(); _relays = null; return; }

                for (int i = 0; i < 4; i++) _relayStates[i] = false;
                UpdateRelayButtonVisual(1, false); UpdateRelayButtonVisual(2, false); UpdateRelayButtonVisual(3, false);

                _watcher = new JournalWatcher(txtJournalPath.Text);
                _onDebugLine = s => AppendLog(s);
                _onHeatWarning = () =>
                {
                    if (!_running || _relays == null) return;
                    if (TryActivateFog(FogRelayIndex, 3000, "HeatWarning")) { SetFogActive(3000); AppendLog("HeatWarning triggered fog (3s)"); }
                };
                _onHeatDamage = () =>
                {
                    if (!_running || _relays == null) return;
                    if (TryActivateFog(FogRelayIndex, 5000, "HeatDamage")) { SetFogActive(5000); AppendLog("HeatDamage triggered fog (5s)"); }
                };

                _watcher.DebugLine += _onDebugLine;
                _watcher.HeatWarning += _onHeatWarning;
                _watcher.HeatDamage += _onHeatDamage;

                _watcher.Start();
                _running = true;
                btnStartStop.Text = "Stop";
                SetManualButtonsEnabled(true);

                try
                {
                    if (Directory.Exists(txtJournalPath.Text))
                    {
                        var files = Directory.GetFiles(txtJournalPath.Text, "*.journal*");
                        if (files.Length > 0)
                        {
                            var latest = files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                            AppendLog($"Game journals found: {Path.GetFileName(latest)}");
                        }
                        else AppendLog("No journal files found in configured folder.");
                    }
                    else AppendLog("Configured journal folder does not exist.");
                }
                catch (Exception ex) { AppendLog($"Journal check failed: {ex.Message}"); }

                RegisterHotKeys();
                cmbMode.Enabled = false;
                AppendLog("Watcher started (Stand Alone).");
#if EMBED_HTTP_SERVER
                StartEmbeddedServer(5000);
#endif
            }
            else // Game PC
            {
                _watcher = new JournalWatcher(txtJournalPath.Text);
                _onDebugLine = s => AppendLog(s);
                _onHeatWarning = () =>
                {
                    if (!_running) return;
                    AppendLog("HeatWarning detected (Game PC)");
                    EnsureForwarderCreated();
                    if (_forwarder != null)
                    {
                        _ = _forwarder.SendFogAsync(FogRelayIndex, 3000).ContinueWith(t =>
                        {
                            if (t.Result)
                            {
                                if (IsHandleCreated)
                                {
                                    if (InvokeRequired) BeginInvoke((Action)(() => SetFogActive(3000)));
                                    else SetFogActive(3000);
                                }
                                AppendLog("Forwarded: HeatWarning -> Fog 3s");
                            }
                            else AppendLog("Forward failed: HeatWarning");
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                };
                _onHeatDamage = () =>
                {
                    if (!_running) return;
                    AppendLog("HeatDamage detected (Game PC)");
                    EnsureForwarderCreated();
                    if (_forwarder != null)
                    {
                        _ = _forwarder.SendFogAsync(FogRelayIndex, 5000).ContinueWith(t =>
                        {
                            if (t.Result)
                            {
                                if (IsHandleCreated)
                                {
                                    if (InvokeRequired) BeginInvoke((Action)(() => SetFogActive(5000)));
                                    else SetFogActive(5000);
                                }
                                AppendLog("Forwarded: HeatDamage -> Fog 5s");
                            }
                            else AppendLog("Forward failed: HeatDamage");
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                };

                _watcher.DebugLine += _onDebugLine;
                _watcher.HeatWarning += _onHeatWarning;
                _watcher.HeatDamage += _onHeatDamage;

                EnsureForwarderCreated();
                _watcher.Start();
                _running = true;
                btnStartStop.Text = "Stop";
                btnFog3.Enabled = true; btnFog5.Enabled = true;
                btnR2.Enabled = true; btnR3.Enabled = true; btnR4.Enabled = true;
                cmbMode.Enabled = false;
                AppendLog("Watcher started (Game PC).");
            }
        }
        else
        {
            btnStartStop.Enabled = false;
            AppendLog("Stopping watcher and cancelling all activity...");
            _running = false;

            if (_watcher != null)
            {
                if (_onDebugLine != null) _watcher.DebugLine -= _onDebugLine;
                if (_onHeatWarning != null) _watcher.HeatWarning -= _onHeatWarning;
                if (_onHeatDamage != null) _watcher.HeatDamage -= _onHeatDamage;
            }
            if (_watcher != null) await _watcher.StopAsync();
            _watcher = null;

            UnregisterHotKeys();
            _relays?.Dispose();
            _relays = null;
            _forwarder?.Dispose();
            _forwarder = null;
#if EMBED_HTTP_SERVER
            _ = StopEmbeddedServerAsync();
#endif
            lock (_activationLock) { _lastActivationUtc = DateTime.MinValue; }
            for (int i = 0; i < 4; i++) _relayStates[i] = false;
            UpdateRelayButtonVisual(1, false); UpdateRelayButtonVisual(2, false); UpdateRelayButtonVisual(3, false);
            SetManualButtonsEnabled(false);
            btnStartStop.Text = "Start";
            btnStartStop.Enabled = true;
            cmbMode.Enabled = true;
            AppendLog("Stopped and all functions cancelled.");
        }
    }

    private void RegisterHotKeys()
    {
        try
        {
            var mods = MOD_CONTROL | MOD_ALT;
            if (!RegisterHotKey(this.Handle, HOT_R1_3, mods, (uint)Keys.D1)) AppendLog("Failed to register hotkey R1 3s.");
            if (!RegisterHotKey(this.Handle, HOT_R1_5, mods, (uint)Keys.D6)) AppendLog("Failed to register hotkey R1 5s.");
            if (!RegisterHotKey(this.Handle, HOT_R2_TOGGLE, mods, (uint)Keys.D2)) AppendLog("Failed to register hotkey R2 toggle.");
            if (!RegisterHotKey(this.Handle, HOT_R3_TOGGLE, mods, (uint)Keys.D3)) AppendLog("Failed to register hotkey R3 toggle.");
            if (!RegisterHotKey(this.Handle, HOT_R4_TOGGLE, mods, (uint)Keys.D4)) AppendLog("Failed to register hotkey R4 toggle.");
            if (!RegisterHotKey(this.Handle, HOT_STARTSTOP, mods, (uint)Keys.D5)) AppendLog("Failed to register hotkey Start/Stop.");
            AppendLog("Hotkeys registered (Ctrl+Alt+F9/F10/NumPad1-3, Ctrl+Alt+5).");
        }
        catch (Exception ex) { AppendLog($"RegisterHotKeys error: {ex.Message}"); }
    }

    private void UnregisterHotKeys()
    {
        try
        {
            UnregisterHotKey(this.Handle, HOT_R1_3);
            UnregisterHotKey(this.Handle, HOT_R1_5);
            UnregisterHotKey(this.Handle, HOT_R2_TOGGLE);
            UnregisterHotKey(this.Handle, HOT_R3_TOGGLE);
            UnregisterHotKey(this.Handle, HOT_R4_TOGGLE);
            UnregisterHotKey(this.Handle, HOT_STARTSTOP);
            AppendLog("Hotkeys unregistered.");
        }
        catch { }
    }

    private bool TryActivateFog(int relayIndex, int durationMs, string reason)
    {
        lock (_activationLock)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastActivationUtc;
            if (elapsed < ActivationCooldown)
            {
                var remaining = ActivationCooldown - elapsed;
                AppendLog($"{reason} ignored: on cooldown ({remaining.TotalSeconds:F1}s remaining)");
                return false;
            }
            _lastActivationUtc = now;
        }
        try { _relays?.FogBlast(relayIndex, durationMs); return true; }
        catch (Exception ex) { AppendLog($"Failed to activate fog: {ex.Message}"); return false; }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            switch (id)
            {
                case HOT_R1_3: if (_running) OnManualFogClicked(FogRelayIndex, 3000); break;
                case HOT_R1_5: if (_running) OnManualFogClicked(FogRelayIndex, 5000); break;
                case HOT_R2_TOGGLE: if (_running) ToggleRelay(1); break;
                case HOT_R3_TOGGLE: if (_running) ToggleRelay(2); break;
                case HOT_R4_TOGGLE: if (_running) ToggleRelay(3); break;
                case HOT_STARTSTOP: btnStartStop.PerformClick(); break;
            }
        }
        base.WndProc(ref m);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        Settings.Default.JournalPath = txtJournalPath.Text;
        Settings.Default.RelayAddress = txtRelayAddress.Text.Trim();
        Settings.Default.Save();
        UnregisterHotKeys();
        if (_watcher != null) { _ = _watcher.StopAsync(); _watcher = null; }
        _relays?.Dispose(); _relays = null;
        _forwarder?.Dispose(); _forwarder = null;
        _fogActiveCts?.Cancel(); _fogActiveCts?.Dispose(); _fogActiveCts = null;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(message))); return; }
        txtLog.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        txtLog.SelectionStart = txtLog.Text.Length;
        txtLog.ScrollToCaret();
    }

    private void UpdateRelayButtonVisual(int relayIndex, bool isOn)
    {
        Button? btn = relayIndex switch { 1 => btnR2, 2 => btnR3, 3 => btnR4, _ => null };
        if (btn == null) return;

        if (isOn)
        {
            btn.Text = $"R{relayIndex + 1}: ON";
            btn.BackColor = EdOrange;
            btn.ForeColor = EdText;
            btn.FlatAppearance.BorderColor = Color.FromArgb(200, 110, 0);
        }
        else
        {
            btn.Text = $"R{relayIndex + 1}: OFF";
            btn.BackColor = EdPanel;
            btn.ForeColor = EdOrange;
            btn.FlatAppearance.BorderColor = EdOrange;
        }
    }

#if EMBED_HTTP_SERVER
    private Microsoft.AspNetCore.Builder.WebApplication? _embeddedApp;
    private Task? _embeddedAppTask;
    private void StartEmbeddedServer(int port = 5000)
    {
        if (_embeddedApp != null) return;
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions { Args = Array.Empty<string>() });
        var app = builder.Build();
        app.Urls.Add($"http://0.0.0.0:{port}");
        app.MapPost("/api/relay", async (Microsoft.AspNetCore.Http.HttpRequest req, Microsoft.AspNetCore.Http.HttpResponse res) =>
        {
            try
            {
                using var sr = new StreamReader(req.Body);
                var body = await sr.ReadToEndAsync().ConfigureAwait(false);
                AppendLog($"[HTTP] Received: {body}");
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? string.Empty : string.Empty;
                if (string.Equals(action, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = 200;
                    await res.WriteAsJsonAsync(new { result = "ok", action = "ping" }).ConfigureAwait(false);
                    return;
                }
                if (string.Equals(action, "fog", StringComparison.OrdinalIgnoreCase))
                {
                    int relayIndex = root.TryGetProperty("relayIndex", out var ri) && ri.TryGetInt32(out var r) ? r : 0;
                    int durationMs = root.TryGetProperty("durationMs", out var dm) && dm.TryGetInt32(out var d) ? d : 1500;
                    if (_relays != null)
                    {
                        try { _relays.FogBlast(relayIndex, durationMs); SetFogActive(durationMs); res.StatusCode = 200; await res.WriteAsJsonAsync(new { result = "ok", action = "fog", relayIndex, durationMs }).ConfigureAwait(false); }
                        catch (Exception ex) { res.StatusCode = 500; await res.WriteAsync($"Error: {ex.Message}").ConfigureAwait(false); }
                    }
                    else { res.StatusCode = 200; await res.WriteAsJsonAsync(new { result = "simulated", action = "fog" }).ConfigureAwait(false); }
                    return;
                }
                if (string.Equals(action, "setRelay", StringComparison.OrdinalIgnoreCase))
                {
                    int relayIndex = root.TryGetProperty("relayIndex", out var ri2) && ri2.TryGetInt32(out var r2) ? r2 : 0;
                    bool state = root.TryGetProperty("state", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.True;
                    if (_relays != null)
                    {
                        try { _relays.SetRelay(relayIndex, state); UpdateRelayButtonVisual(relayIndex, state); res.StatusCode = 200; await res.WriteAsJsonAsync(new { result = "ok", action = "setRelay", relayIndex, state }).ConfigureAwait(false); }
                        catch (Exception ex) { res.StatusCode = 500; await res.WriteAsync($"Error: {ex.Message}").ConfigureAwait(false); }
                    }
                    else { res.StatusCode = 200; await res.WriteAsJsonAsync(new { result = "simulated", action = "setRelay" }).ConfigureAwait(false); }
                    return;
                }
                res.StatusCode = 400;
                await res.WriteAsync("Unknown action").ConfigureAwait(false);
            }
            catch (Exception ex) { res.StatusCode = 500; await res.WriteAsync($"Server error: {ex.Message}").ConfigureAwait(false); }
        });
        _embeddedApp = app;
        _embeddedAppTask = Task.Run(async () =>
        {
            AppendLog($"Starting embedded HTTP server on port {port}...");
            try { await app.RunAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppendLog($"Embedded server stopped: {ex.Message}"); }
            finally { _embeddedApp = null; _embeddedAppTask = null; }
        });
    }

    private async Task StopEmbeddedServerAsync()
    {
        if (_embeddedApp == null) return;
        try
        {
            AppendLog("Stopping embedded HTTP server...");
            await _embeddedApp.StopAsync().ConfigureAwait(false);
            if (_embeddedApp is IAsyncDisposable adi) await adi.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { AppendLog($"Error stopping embedded server: {ex.Message}"); }
        finally { _embeddedApp = null; _embeddedAppTask = null; }
    }
#endif
}