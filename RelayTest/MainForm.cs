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
    private readonly Button btnSettings;
    private readonly Button btnStartStop;
    private readonly TextBox txtLog;
    private readonly Button btnR1;
    private readonly Button btnR2;
    private readonly Button btnR3;
    private readonly Button btnR4;
    private readonly Label lblInfo; // shows summary of current settings

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
    private readonly DateTime[] _lastActivationUtc = new DateTime[4] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };

    private readonly bool[] _relayStates = new bool[4];

    private readonly Color EdBackground = Color.FromArgb(10, 10, 12);
    private readonly Color EdPanel = Color.FromArgb(20, 20, 24);
    private readonly Color EdOrange = Color.FromArgb(255, 140, 0);
    private readonly Color EdText = Color.FromArgb(230, 230, 230);
    private readonly Color EdBlue = Color.FromArgb(0, 174, 239);

    // Visual countdown tokens / locks per relay
    private readonly CancellationTokenSource?[] _relayVisualCts = new CancellationTokenSource?[4];
    private readonly object[] _relayVisualLocks = new object[4];

    private CancellationTokenSource? _fogActiveCts;

    // UI scale factor derived at runtime from device DPI (96 = 100%)
    private readonly float _uiScale;

#if EMBED_HTTP_SERVER
    // Embedded server fields only present when feature enabled
    private Microsoft.AspNetCore.Builder.WebApplication? _embeddedApp;
    private Task? _embeddedAppTask;
#endif

    private int S(int px) => (int)Math.Round(px * _uiScale);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    private const int HOT_R1_SHORT = 1;
    private const int HOT_R1_LONG = 2;
    private const int HOT_R2_TOGGLE = 3;
    private const int HOT_R3_TOGGLE = 4;
    private const int HOT_R4_TOGGLE = 5;
    private const int HOT_STARTSTOP = 6;

    public MainForm()
    {
        // Compute UI scale from current device DPI (fallback to 1.0)
        float dpi = 96f;
        try
        {
            using (var g = CreateGraphics())
            {
                dpi = g.DpiX;
            }
        }
        catch { dpi = 96f; }
        _uiScale = Math.Max(0.5f, dpi / 96f);

        // Let WinForms perform DPI-aware autoscaling
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Simstarr Relay Control";
        Width = S(900);
        Height = S(600);
        MinimumSize = new Size(S(700), S(420));

        BackColor = EdBackground;
        ForeColor = EdText;

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 9,
            RowCount = 2,
            Padding = new Padding(S(8)),
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

        // Settings button (opens settings dialog). Keep as easy-access.
        btnSettings = new Button
        {
            Text = "SETTINGS...",
            AutoSize = false,
            Size = new Size(S(110), S(44)), // fixed size so content changes don't resize layout
            Margin = new Padding(0, S(4), S(8), S(4)),
            Padding = new Padding(S(6), S(4), S(6), S(4)),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnSettings.FlatAppearance.BorderColor = EdOrange;
        btnSettings.Click += BtnSettings_Click;

        // Info label takes the large center column: shows current relay endpoint and journal path summary.
        lblInfo = new Label
        {
            Text = string.Empty,
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = EdBlue,
            BackColor = EdPanel,
            Margin = new Padding(0, S(6), S(8), S(6))
        };

        btnStartStop = new Button
        {
            Text = "START",
            AutoSize = false,
            Size = new Size(S(110), S(44)),
            Margin = new Padding(0, S(4), S(8), S(4)),
            Padding = new Padding(S(8), S(4), S(8), S(4)),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange
        };
        btnStartStop.FlatAppearance.BorderColor = EdOrange;

        // Relay 1 - 4 buttons (toggle) — fixed, identical sizes so changing text doesn't reflow layout
        var relayBtnSize = new Size(S(160), S(44));
        btnR1 = new Button
        {
            Text = "RLY1: OFF",
            AutoSize = false,
            Size = relayBtnSize,
            Margin = new Padding(0, S(4), S(8), S(4)),
            Padding = new Padding(S(6), S(4), S(6), S(4)),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnR1.FlatAppearance.BorderColor = EdOrange;

        btnR2 = new Button
        {
            Text = "RLY2: OFF",
            AutoSize = false,
            Size = relayBtnSize,
            Margin = new Padding(0, S(4), S(8), S(4)),
            Padding = new Padding(S(6), S(4), S(6), S(4)),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnR2.FlatAppearance.BorderColor = EdOrange;

        btnR3 = new Button
        {
            Text = "RLY3: OFF",
            AutoSize = false,
            Size = relayBtnSize,
            Margin = new Padding(0, S(4), S(8), S(4)),
            Padding = new Padding(S(6), S(4), S(6), S(4)),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnR3.FlatAppearance.BorderColor = EdOrange;

        btnR4 = new Button
        {
            Text = "RLY4: OFF",
            AutoSize = false,
            Size = relayBtnSize,
            Margin = new Padding(0, S(4), 0, S(4)),
            Padding = new Padding(S(6), S(4), S(6), S(4)),
            FlatStyle = FlatStyle.Flat,
            BackColor = EdPanel,
            ForeColor = EdOrange,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnR4.FlatAppearance.BorderColor = EdOrange;

        // Place controls in layout.
        topLayout.Controls.Add(btnSettings, 0, 0);
        topLayout.SetRowSpan(btnSettings, 2);
        topLayout.Controls.Add(lblInfo, 1, 0);
        topLayout.SetRowSpan(lblInfo, 2);

        // Place Start/Stop and Relay buttons across the top row
        topLayout.Controls.Add(btnStartStop, 2, 0);
        topLayout.Controls.Add(btnR1, 3, 0);
        topLayout.Controls.Add(btnR2, 4, 0);
        topLayout.Controls.Add(btnR3, 5, 0);
        topLayout.Controls.Add(btnR4, 6, 0);

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f * _uiScale),
            BackColor = EdBackground,
            ForeColor = EdOrange,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(txtLog);
        Controls.Add(topLayout);

        // Load saved values into local UI
        _mode = Settings.Default.AppMode switch
        {
            "Relay" => AppMode.Relay,
            "Game" => AppMode.Game,
            _ => AppMode.StandAlone
        };

        UpdateInfoDisplay();

        btnStartStop.Click += BtnStartStop_Click;
        btnR1.Click += (s, e) => ToggleRelay(0);
        btnR2.Click += (s, e) => ToggleRelay(1);
        btnR3.Click += (s, e) => ToggleRelay(2);
        btnR4.Click += (s, e) => ToggleRelay(3);

        FormClosing += MainForm_FormClosing;

        btnSettings.TabIndex = 0;
        btnStartStop.TabIndex = 1;
        btnR1.TabIndex = 2;
        btnR2.TabIndex = 3;
        btnR3.TabIndex = 4;
        btnR4.TabIndex = 5;

        // initialize per-relay visual locks
        for (int i = 0; i < 4; i++) _relayVisualLocks[i] = new object();

        SetManualButtonsEnabled(false);
    }

    private void BtnSettings_Click(object? sender, EventArgs e)
    {
        using var dlg = new SettingsForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            // Apply any changed settings
            UpdateInfoDisplay();
            AppendLog("Settings updated.");
        }
    }

    private void UpdateInfoDisplay()
    {
        try
        {
            var journal = string.IsNullOrEmpty(Settings.Default.JournalPath) ? "(no journal folder)" : Settings.Default.JournalPath;

            switch (_mode)
            {
                case AppMode.StandAlone:
                    // Stand Alone uses local hardware — do not try to show or contact a remote relay endpoint.
                    lblInfo.Text = $"Mode: Stand Alone\nRelay: Local hardware\nJournal: {journal}";
                    break;

                case AppMode.Relay:
                    // Relay PC hosts the hardware. Show local IPs so a Game PC can connect to this machine.
                    var localIps = GetLocalIpv4();
                    var ipsText = localIps.Length == 0 ? "(no IPv4 addresses found)" : string.Join(", ", localIps);
                    var primaryIp = GetPrimaryIpv4() ?? "(unknown)";
                    lblInfo.Text = $"Mode: Relay PC\nLocal IPs: {ipsText}\nPrimary IP: {primaryIp}\nJournal: {journal}";
                    break;

                default: // AppMode.Game
                    // Game PC mode uses a configured relay endpoint — show the normalized endpoint if present.
                    var norm = NormalizeRelayAddress(Settings.Default.RelayAddress);
                    if (string.IsNullOrEmpty(norm))
                    {
                        lblInfo.Text = $"Mode: Game PC\nRelay Endpoint: (none)\nJournal: {journal}";
                    }
                    else
                    {
                        var plain = norm.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            ? norm.Substring("http://".Length)
                            : (norm.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? norm.Substring("https://".Length) : norm);

                        lblInfo.Text = $"Mode: Game PC\nRelay Endpoint: {norm}\nGame PC can enter: {plain}\nJournal: {journal}";
                    }
                    break;
            }
        }
        catch
        {
            lblInfo.Text = "Settings summary";
        }
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
        // Enable manual buttons whenever running in any mode
        SetManualButtonsEnabled(_running);
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
                    !ip.StartsWith("127.") &&
                    !ip.StartsWith("169.254.") &&
                    ip != "0.0.0.0")
                .OrderBy(ip =>
                {
                    if (ip.StartsWith("192.168.")) return 0;
                    if (ip.StartsWith("10.")) return 1;
                    if (ip.StartsWith("172.")) return 2;
                    return 3;
                })
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private bool IsLocalRelayMode() => _mode == AppMode.Relay || _mode == AppMode.StandAlone;

    private void SetManualButtonsEnabled(bool enabled)
    {
        btnR1.Enabled = enabled;
        btnR2.Enabled = enabled;
        btnR3.Enabled = enabled;
        btnR4.Enabled = enabled;
    }

    // Per-relay cooldown check. Returns true and records activation time if allowed.
    private bool TryBeginActivation(int relayIndex, string reason)
    {
        lock (_activationLock)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastActivationUtc[relayIndex];
            if (elapsed < ActivationCooldown)
            {
                var remaining = ActivationCooldown - elapsed;
                AppendLog($"{reason} (Relay {relayIndex + 1}) ignored: on cooldown ({remaining.TotalSeconds:F1}s remaining)");
                return false;
            }
            _lastActivationUtc[relayIndex] = now;
            return true;
        }
    }

    // Backwards-compatible wrapper (defaults to relay 0 visual)
    private void SetFogActive(int durationMs) => StartRelayVisualCountdown(0, durationMs);

    // Visual countdown + cooldown display for a specific relay button.
    // - Turns the button blue while the relay is active and shows remaining active seconds.
    // - After activation ends, shows the global cooldown remaining until next activation is allowed.
    private async void StartRelayVisualCountdown(int relayIndex, int durationMs)
    {
        if (relayIndex < 0 || relayIndex > 3) relayIndex = 0;

        // Acquire per-relay cancellation token
        CancellationTokenSource? cts;
        lock (_relayVisualLocks[relayIndex])
        {
            try { _relayVisualCts[relayIndex]?.Cancel(); } catch { }
            try { _relayVisualCts[relayIndex]?.Dispose(); } catch { }
            _relayVisualCts[relayIndex] = new CancellationTokenSource();
            cts = _relayVisualCts[relayIndex];
        }

        if (cts == null) return;
        var ct = cts.Token;
        Button btn = relayIndex switch { 0 => btnR1, 1 => btnR2, 2 => btnR3, 3 => btnR4, _ => btnR1 };

        void UpdateUi(string text, Color back, Color fore)
        {
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() =>
                {
                    btn.Text = text;
                    btn.BackColor = back;
                    btn.ForeColor = fore;
                }));
                else
                {
                    btn.Text = text;
                    btn.BackColor = back;
                    btn.ForeColor = fore;
                }
            }
        }

        try
        {
            lock (_activationLock)
            {
                _relayStates[relayIndex] = true;
            }
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() => UpdateRelayButtonVisual(relayIndex, true)));
                else UpdateRelayButtonVisual(relayIndex, true);
            }

            // Activation countdown (show whole seconds, uppercase)
            var activationEnd = DateTime.UtcNow + TimeSpan.FromMilliseconds(durationMs);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var remain = activationEnd - DateTime.UtcNow;
                if (remain <= TimeSpan.Zero) break;
                var seconds = Math.Max(0, (int)Math.Ceiling(remain.TotalSeconds));
                UpdateUi($"RLY{relayIndex + 1}: {seconds}s".ToUpperInvariant(), EdBlue, Color.Black);
                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            // After activation expires (or was cancelled), turn OFF the relay hardware
            var isOn = false;
            lock (_activationLock)
            {
                _relayStates[relayIndex] = isOn;
            }
            
            // Turn off the hardware relay if in local mode
            if (IsLocalRelayMode() && _relays != null)
            {
                try
                {
                    _relays.SetRelay(relayIndex, false);
                }
                catch (Exception ex)
                {
                    AppendLog($"Failed to turn off relay {relayIndex + 1}: {ex.Message}");
                }
            }
            
            UpdateUi($"RLY{relayIndex + 1}: {(isOn ? "ON" : "OFF")}".ToUpperInvariant(), EdPanel, isOn ? EdOrange : EdText);

            // Reset the cooldown timer so it starts from when the relay actually turns off
            lock (_activationLock)
            {
                _lastActivationUtc[relayIndex] = DateTime.UtcNow;
            }

            // Show per-relay cooldown remaining (based on Settings value) — whole seconds, grey font
            var cooldownMs = Settings.Default.ActivationCooldownMs;
            var cooldownEnd = DateTime.UtcNow + TimeSpan.FromMilliseconds(cooldownMs);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var rem = cooldownEnd - DateTime.UtcNow;
                if (rem <= TimeSpan.Zero) break;
                var csecs = Math.Max(0, (int)Math.Ceiling(rem.TotalSeconds));
                UpdateUi($"RLY{relayIndex + 1}: CDWN {csecs}s".ToUpperInvariant(), EdPanel, Color.Gray);
                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            // Final restore
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() => UpdateRelayButtonVisual(relayIndex, isOn)));
                else UpdateRelayButtonVisual(relayIndex, isOn);
            }
        }
        catch (OperationCanceledException)
        {
            // Button was clicked during countdown — skip directly to cooldown
            var isOn = false;
            lock (_activationLock)
            {
                _relayStates[relayIndex] = isOn;
            }
            
            // Turn off the hardware relay immediately
            if (IsLocalRelayMode() && _relays != null)
            {
                try
                {
                    _relays.SetRelay(relayIndex, false);
                }
                catch (Exception ex)
                {
                    AppendLog($"Failed to turn off relay {relayIndex + 1}: {ex.Message}");
                }
            }
            
            UpdateUi($"RLY{relayIndex + 1}: {(isOn ? "ON" : "OFF")}".ToUpperInvariant(), EdPanel, isOn ? EdOrange : EdText);

            // Reset cooldown timer
            lock (_activationLock)
            {
                _lastActivationUtc[relayIndex] = DateTime.UtcNow;
            }

            // Show cooldown immediately
            try
            {
                var cooldownMs = Settings.Default.ActivationCooldownMs;
                var cooldownEnd = DateTime.UtcNow + TimeSpan.FromMilliseconds(cooldownMs);
                while (true)
                {
                    var rem = cooldownEnd - DateTime.UtcNow;
                    if (rem <= TimeSpan.Zero) break;
                    var csecs = Math.Max(0, (int)Math.Ceiling(rem.TotalSeconds));
                    UpdateUi($"RLY{relayIndex + 1}: CDWN {csecs}s".ToUpperInvariant(), EdPanel, Color.Gray);
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                }
                
                // Final restore
                if (IsHandleCreated)
                {
                    if (InvokeRequired) BeginInvoke((Action)(() => UpdateRelayButtonVisual(relayIndex, isOn)));
                    else UpdateRelayButtonVisual(relayIndex, isOn);
                }
            }
            catch { }
        }
        catch (Exception) { }
        finally
        {
            lock (_relayVisualLocks[relayIndex])
            {
                try { _relayVisualCts[relayIndex]?.Dispose(); } catch { }
                _relayVisualCts[relayIndex] = null;
            }
        }
    }

    private async void OnManualFogClicked(int relayIndex, int durationMs)
    {
        if (!TryBeginActivation(relayIndex, "Fog")) return;

        if (IsLocalRelayMode())
        {
            if (_relays == null) { AppendLog("Relay not initialized."); return; }

            // Use configured per-relay duration when available
            var dur = (Settings.Default.RelayActivateMs.Length > relayIndex) ? Settings.Default.RelayActivateMs[relayIndex] : durationMs;

            // Trigger the hardware fog blast so the relay actually turns on and then off.
            try
            {
                _relays.FogBlast(relayIndex, dur);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to trigger fog on relay {relayIndex + 1}: {ex.Message}");
                return;
            }

            // Start the visual countdown for the user
            StartRelayVisualCountdown(relayIndex, dur);
            AppendLog($"Manual: Relay {relayIndex + 1} Fog {dur / 1000}s");
            return;
        }

        if (!_running) { AppendLog("Not running."); return; }
        EnsureForwarderCreated();
        if (_forwarder == null) { AppendLog("Forwarder not available."); return; }

        bool ok = false;
        if (_forwarder != null)
        {
            ok = await _forwarder.SendFogAsync(relayIndex, durationMs).ConfigureAwait(false);
        }

        if (ok)
        {
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(relayIndex, durationMs)));
                else StartRelayVisualCountdown(relayIndex, durationMs);
            }
            AppendLog($"Forwarded: Fog relay {relayIndex + 1} {durationMs / 1000}s");
        }
        else AppendLog("Forward failed: Fog");
    }

    private async void ToggleRelay(int relayIndex)
    {
        // Toggle remains immediate for turning OFF, but turning ON must respect per-relay cooldown.
        if (IsLocalRelayMode())
        {
            if (_relays == null) { AppendLog("Relay not initialized."); return; }

            DateTime prevLast = DateTime.MinValue;
            bool current;
            lock (_activationLock)
            {
                current = _relayStates[relayIndex];
            }
            var desiredState = !current;

            // If trying to turn ON, enforce cooldown
            if (desiredState)
            {
                lock (_activationLock) { prevLast = _lastActivationUtc[relayIndex]; } // remember so we can undo if hardware call fails
                if (!TryBeginActivation(relayIndex, "ManualToggle"))
                {
                    // TryBeginActivation already logs the cooldown; leave state unchanged
                    return;
                }
            }

            try
            {
                // Update local state and hardware
                lock (_activationLock) { _relayStates[relayIndex] = desiredState; }
                _relays.SetRelay(relayIndex, desiredState);
                UpdateRelayButtonVisual(relayIndex, desiredState);
                AppendLog($"Relay {relayIndex + 1} turned {(desiredState ? "ON" : "OFF")} (local)");

                // If turned ON, start activation visuals (TryBeginActivation already set _lastActivationUtc[relayIndex])
                if (desiredState)
                {
                    var dur = (Settings.Default.RelayActivateMs.Length > relayIndex) ? Settings.Default.RelayActivateMs[relayIndex] : Settings.Default.FogShortMs;
                    if (IsHandleCreated)
                    {
                        if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(relayIndex, dur)));
                        else StartRelayVisualCountdown(relayIndex, dur);
                    }
                }
            }
            catch (Exception ex)
            {
                // Revert local state and cooldown timestamp if hardware call failed after we claimed activation
                lock (_activationLock)
                {
                    _relayStates[relayIndex] = current;
                    if (desiredState) _lastActivationUtc[relayIndex] = prevLast;
                }
                AppendLog($"Failed to toggle relay {relayIndex + 1}: {ex.Message}");
            }

            return;
        }

        // Remote / Game PC handling
        if (!_running) { AppendLog("Not running."); return; }
        EnsureForwarderCreated();
        if (_forwarder == null) { AppendLog("Forwarder not available."); return; }

        bool prev;
        lock (_activationLock) { prev = _relayStates[relayIndex]; }
        bool desired = !prev;

        // enforce cooldown for turning ON
        DateTime prevLastActivation = DateTime.MinValue;
        if (desired)
        {
            lock (_activationLock) { prevLastActivation = _lastActivationUtc[relayIndex]; }
            if (!TryBeginActivation(relayIndex, "ManualToggle"))
            {
                return;
            }
        }

        var ok = await _forwarder.SendSetRelayAsync(relayIndex, desired).ConfigureAwait(false);
        if (ok)
        {
            lock (_activationLock) { _relayStates[relayIndex] = desired; }
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() => UpdateRelayButtonVisual(relayIndex, desired)));
                else UpdateRelayButtonVisual(relayIndex, desired);
            }
            AppendLog($"Forwarded: SetRelay {relayIndex + 1} -> {(desired ? "ON" : "OFF")} (relay={_relayStates[relayIndex]})");

            if (desired)
            {
                var dur = (Settings.Default.RelayActivateMs.Length > relayIndex) ? Settings.Default.RelayActivateMs[relayIndex] : Settings.Default.FogShortMs;
                if (IsHandleCreated)
                {
                    if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(relayIndex, dur)));
                    else StartRelayVisualCountdown(relayIndex, dur);
                }
            }
        }
        else
        {
            // revert cooldown timestamp if we claimed it but forward failed
            if (desired)
            {
                lock (_activationLock) { _lastActivationUtc[relayIndex] = prevLastActivation; }
            }
            AppendLog($"Forward failed: SetRelay {relayIndex + 1}");
        }
    }

    private void EnsureForwarderCreated()
    {
        if (_forwarder != null) return;
        try
        {
            var addr = Settings.Default.RelayAddress?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(addr)) { UpdateInfoDisplay(); return; }
            addr = NormalizeRelayAddress(addr);

            _forwarder = new EventForwarder(addr, Settings.Default.AuthToken ?? string.Empty);
            AppendLog($"Forwarder created for {addr}");
            UpdateInfoDisplay();
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
            UpdateInfoDisplay();
        }
    }

    private async void BtnStartStop_Click(object? sender, EventArgs e)
    {
        if (!_running)
        {
            if (_mode == AppMode.Relay)
            {
                _forwarder?.Dispose(); _forwarder = null;
                _relays = new RelayController();
                AppendLog("Initializing relay (hardware only)...");

                // New check: if FogShortMs is 0, warn and set to 1500ms default
                if (Settings.Default.FogShortMs <= 0)
                {
                    AppendLog("WARNING: FogShortMs is configured as 0 (disabled). Setting to default 1500ms.");
                    Settings.Default.FogShortMs = 1500;
                    Settings.Default.Save();
                }

                // New: if FogLongMs is 0, treat as infinite (no automatic off)
                if (Settings.Default.FogLongMs <= 0) Settings.Default.FogLongMs = int.MaxValue;

                if (!_relays.Init())
                {
                    AppendLog("Relay init failed.");
                    _relays.Dispose();
                    _relays = null;
                    return;
                }

                for (int i = 0; i < 4; i++) _relayStates[i] = false;
                UpdateRelayButtonVisual(0, false); UpdateRelayButtonVisual(1, false); UpdateRelayButtonVisual(2, false); UpdateRelayButtonVisual(3, false);

                _running = true;
                btnStartStop.Text = "Stop";
                SetManualButtonsEnabled(true);

                RegisterHotKeys();
                AppendLog("Relay hardware initialized (Relay PC). No journal monitoring.");
#if EMBED_HTTP_SERVER
                StartEmbeddedServer(5000);
#endif
            }
            else if (_mode == AppMode.StandAlone)
            {
                _forwarder?.Dispose(); _forwarder = null;
                _relays = new RelayController();
                AppendLog("Initializing relay...");
                if (!_relays.Init()) { AppendLog("Relay init failed."); _relays.Dispose(); _relays = null; return; }

                for (int i = 0; i < 4; i++) _relayStates[i] = false;
                UpdateRelayButtonVisual(0, false); UpdateRelayButtonVisual(1, false); UpdateRelayButtonVisual(2, false); UpdateRelayButtonVisual(3, false);

                _watcher = new JournalWatcher(Settings.Default.JournalPath);
                _onDebugLine = s => AppendLog(s);
                _onHeatWarning = () =>
                {
                    if (!_running || _relays == null) return;
                    var durations = Settings.Default.RelayActivateMs;
                    if (TryActivateAllRelays(durations, "HeatWarning")) { AppendLog($"HeatWarning triggered relays"); }
                };
                _onHeatDamage = () =>
                {
                    if (!_running || _relays == null) return;
                    var durations = Settings.Default.RelayActivateMs;
                    if (TryActivateAllRelays(durations, "HeatDamage")) { AppendLog($"HeatDamage triggered relays"); }
                };

                _watcher.DebugLine += _onDebugLine;
                _watcher.HeatWarning += _onHeatWarning;
                _watcher.HeatDamage += _onHeatDamage;
                
                // Subscribe to custom relay events (one per relay)
                _watcher.CustomRelayEvent += relayIndex =>
                {
                    if (!_running || _relays == null) return;
                    var durations = Settings.Default.RelayActivateMs;
                    
                    // Only activate the specific relay that matched the event
                    if (relayIndex >= 0 && relayIndex < durations.Length)
                    {
                        if (TryBeginActivation(relayIndex, "CustomEvent"))
                        {
                            try
                            {
                                _relays.FogBlast(relayIndex, durations[relayIndex]);
                                AppendLog($"Custom event triggered Relay {relayIndex + 1}");
                                var dur = durations[relayIndex];
                                if (IsHandleCreated)
                                {
                                    if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(relayIndex, dur)));
                                    else StartRelayVisualCountdown(relayIndex, dur);
                                }
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"Failed to activate relay {relayIndex + 1}: {ex.Message}");
                            }
                        }
                    }
                };

                _watcher.Start();
                _running = true;
                btnStartStop.Text = "Stop";
                SetManualButtonsEnabled(true);

                try
                {
                    if (Directory.Exists(Settings.Default.JournalPath))
                    {
                        var files = Directory.GetFiles(Settings.Default.JournalPath, "*.journal*");
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
                AppendLog("Watcher started (Stand Alone).");
#if EMBED_HTTP_SERVER
                StartEmbeddedServer(5000);
#endif
            }
            else // Game PC
            {
                _watcher = new JournalWatcher(Settings.Default.JournalPath);
                _onDebugLine = s => AppendLog(s);
                _onHeatWarning = () =>
                {
                    if (!_running) return;
                    // Check all relays for per-relay cooldown before forwarding
                    var durations = Settings.Default.RelayActivateMs;
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < durations.Length && i < 4; i++)
                        {
                            if (!TryBeginActivation(i, "HeatWarning")) continue;
                            AppendLog($"HeatWarning detected - forwarding Relay {i + 1}");
                            var dur = durations[i];
                            bool ok = false;
                            if (_forwarder != null)
                            {
                                ok = await _forwarder.SendFogAsync(i, dur).ConfigureAwait(false);
                            }
                            if (ok)
                            {
                                if (IsHandleCreated)
                                {
                                    if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(i, dur)));
                                    else StartRelayVisualCountdown(i, dur);
                                }
                            }
                        }
                    });
                };
                _onHeatDamage = () =>
                {
                    if (!_running) return;
                    // Check all relays for per-relay cooldown before forwarding
                    var durations = Settings.Default.RelayActivateMs;
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < durations.Length && i < 4; i++)
                        {
                            if (!TryBeginActivation(i, "HeatDamage")) continue;
                            AppendLog($"HeatDamage detected - forwarding Relay {i + 1}");
                            var dur = durations[i];
                            bool ok = false;
                            if (_forwarder != null)
                            {
                                ok = await _forwarder.SendFogAsync(i, dur).ConfigureAwait(false);
                            }
                            if (ok)
                            {
                                if (IsHandleCreated)
                                {
                                    if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(i, dur)));
                                    else StartRelayVisualCountdown(i, dur);
                                }
                            }
                        }
                    });
                };

                _watcher.DebugLine += _onDebugLine;
                _watcher.HeatWarning += _onHeatWarning;
                _watcher.HeatDamage += _onHeatDamage;

                // Subscribe to custom relay events (forward them to relay PC)
                _watcher.CustomRelayEvent += relayIndex =>
                {
                    if (!_running) return;
                    var durations = Settings.Default.RelayActivateMs;
                    
                    // Only forward the specific relay that matched the event
                    if (relayIndex >= 0 && relayIndex < durations.Length)
                    {
                        if (TryBeginActivation(relayIndex, "CustomEvent"))
                        {
                            _ = Task.Run(async () =>
                            {
                                var dur = durations[relayIndex];
                                AppendLog($"Custom event detected - forwarding Relay {relayIndex + 1}");
                                bool ok = false;
                                if (_forwarder != null)
                                {
                                    ok = await _forwarder.SendFogAsync(relayIndex, dur).ConfigureAwait(false);
                                }
                                if (ok)
                                {
                                    if (IsHandleCreated)
                                    {
                                        if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(relayIndex, dur)));
                                        else StartRelayVisualCountdown(relayIndex, dur);
                                    }
                                }
                            });
                        }
                    }
                };

                EnsureForwarderCreated();
                _watcher.Start();
                _running = true;
                btnStartStop.Text = "Stop";
                SetManualButtonsEnabled(true); // Game PC CAN manually toggle relays via forwarder (same as Stand Alone)
                RegisterHotKeys(); // Game PC can also use hotkeys to control relays
                AppendLog("Journal watcher started (Game PC - forwarding to Relay).");
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
#else
            _ = StopEmbeddedServerAsync();
#endif
            lock (_activationLock) { for (int i = 0; i < 4; i++) _lastActivationUtc[i] = DateTime.MinValue; }
            for (int i = 0; i < 4; i++) _relayStates[i] = false;
            UpdateRelayButtonVisual(0, false); UpdateRelayButtonVisual(1, false); UpdateRelayButtonVisual(2, false); UpdateRelayButtonVisual(3, false);
            SetManualButtonsEnabled(false);
            btnStartStop.Text = "Start";
            btnStartStop.Enabled = true;
            AppendLog("Stopped and all functions cancelled.");
        }
    }

    // New helper: try activate all relays locally (uses RelayController.FogBlast per relay)
    private bool TryActivateAllRelays(int[] durationsMs, string reason)
    {
        try
        {
            if (_relays == null) { AppendLog("Relay not initialized."); return false; }
            for (int i = 0; i < durationsMs.Length && i < 4; i++)
            {
                // Per-relay cooldown check
                if (!TryBeginActivation(i, reason)) continue;

                try
                {
                    _relays.FogBlast(i, durationsMs[i]);
                }
                catch (Exception exInner)
                {
                    AppendLog($"Failed to activate relay {i + 1}: {exInner.Message}");
                }
            }

            // Visual feedback: start visual countdown for relay 1
            var r1Dur = durationsMs.Length > 0 ? durationsMs[0] : Settings.Default.FogShortMs;
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(0, r1Dur)));
                else StartRelayVisualCountdown(0, r1Dur);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to activate relays: {ex.Message}");
            return false;
        }
    }

    // New helper: forward fog activations to remote relay endpoint for all relays
    private async Task ForwardFogAllAsync(int[] durationsMs, string? reason = null, int relayIndex = 0)
    {
        if (_forwarder == null) { AppendLog("Forwarder not available."); return; }
        if (durationsMs.Length == 0) return;

        try
        {
            // fire-and-forget: don't await
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < durationsMs.Length && i < 4; i++)
                    {
                        var dur = durationsMs[i];
                        var ok = await _forwarder.SendFogAsync(i, dur).ConfigureAwait(false);
                        if (ok)
                        {
                            if (IsHandleCreated)
                            {
                                if (InvokeRequired) BeginInvoke((Action)(() => StartRelayVisualCountdown(i, dur)));
                                else StartRelayVisualCountdown(i, dur);
                            }
                            AppendLog($"Forwarded: Fog relay {i + 1} {dur / 1000}s");
                        }
                        else AppendLog("Forward failed: Fog");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Forward all failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"Forward helper failed: {ex.Message}");
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // Hotkey handling: relay-specific actions bypass normal cooldowns
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            uint mod = (uint)(m.LParam.ToInt32() >> 16);
            uint vk = (uint)(m.LParam.ToInt32() & 0xFFFF);

            // Log all hotkey triggers (includes modifiers)
            AppendLog($"Hotkey {id} triggered: mod={mod:X} vk={vk:X}");

            try
            {
                // Relay 1..4 toggle (with cooldown bypass)
                if (id >= HOT_R2_TOGGLE && id <= HOT_R4_TOGGLE)
                {
                    int relayIndex = id - HOT_R2_TOGGLE + 1;
                    ToggleRelay(relayIndex);
                    return;
                }

                switch (id)
                {
                    case HOT_R1_SHORT:
                        // Relay 1 short fog (manual, direct to relay)
                        OnManualFogClicked(0, Settings.Default.FogShortMs);
                        return;

                    case HOT_R1_LONG:
                        // Relay 1 long fog (manual, direct to relay)
                        OnManualFogClicked(0, Settings.Default.FogLongMs);
                        return;

                    case HOT_STARTSTOP:
                        btnStartStop.PerformClick();
                        return;

                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Hotkey action error: {ex.Message}");
            }
        }
    }

    private void RegisterHotKeys()
    {
        try { UnregisterHotKeys(); } catch { }

        // Register hotkeys for relays 1-4 (no modifiers, just the keys)
        for (int i = 0; i < 4; i++)
        {
            int id = HOT_R2_TOGGLE + i;
            uint vk = (uint)(Keys.D2 + i);
            RegisterHotKey(Handle, id, 0, vk);
        }

        AppendLog("Hotkeys registered.");
    }

    private void UnregisterHotKeys()
    {
        // Unregister all hotkeys registered by this form.
        for (int i = 0; i < 4; i++)
        {
            int id = HOT_R2_TOGGLE + i;
            UnregisterHotKey(Handle, id);
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // Persist settings and clean up resources used by the form.
        try
        {
            Settings.Default.Save();
        }
        catch { /* non-fatal */ }

        try { UnregisterHotKeys(); } catch { }

        if (_watcher != null)
        {
            try { _ = _watcher.StopAsync(); } catch { }
            _watcher = null;
        }

        try { _relays?.Dispose(); } catch { }
        _relays = null;

        try { _forwarder?.Dispose(); } catch { }
        _forwarder = null;

        try { _fogActiveCts?.Cancel(); _fogActiveCts?.Dispose(); } catch { }
        _fogActiveCts = null;

        for (int i = 0; i < _relayVisualCts.Length; i++)
        {
            try { _relayVisualCts[i]?.Cancel(); _relayVisualCts[i]?.Dispose(); } catch { }
            _relayVisualCts[i] = null;
        }
    }

    // Added missing UI helpers and embedded server wrappers so references compile.
    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(message))); return; }
        if (txtLog == null) return;
        txtLog.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        txtLog.SelectionStart = txtLog.Text.Length;
        txtLog.ScrollToCaret();
    }

    private void UpdateRelayButtonVisual(int relayIndex, bool isOn)
    {
        Button? btn = relayIndex switch { 0 => btnR1, 1 => btnR2, 2 => btnR3, 3 => btnR4, _ => null };
        if (btn == null) return;

        var text = $"RLY{relayIndex + 1}: {(isOn ? "ON" : "OFF")}";
        btn.Text = text.ToUpperInvariant();

        if (isOn)
        {
            // ON: panel background, orange text
            btn.ForeColor = EdOrange;
            try { btn.FlatAppearance.BorderColor = Color.FromArgb(200, 110, 0); } catch { }
            btn.BackColor = EdPanel;
        }
        else
        {
            // OFF: black text for contrast on orange background
            btn.ForeColor = Color.Black;
            try { btn.FlatAppearance.BorderColor = EdOrange; } catch { }
            btn.BackColor = EdOrange;
        }
    }

    // Start/Stop embedded server wrappers. Present regardless of build symbol.
    private void StartEmbeddedServer(int port = 5000)
    {
#if EMBED_HTTP_SERVER
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
                        try { _relays.FogBlast(relayIndex, durationMs); StartRelayVisualCountdown(relayIndex, durationMs); res.StatusCode = 200; await res.WriteAsJsonAsync(new { result = "ok", action = "fog", relayIndex, durationMs }).ConfigureAwait(false); }
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
                    else { res.StatusCode = 200; await res.WriteAsJsonAsync(new { result = "simulated", action = "setRelay" }). ConfigureAwait(false); }
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
#else
    AppendLog("Embedded HTTP server is not enabled in this build.");
#endif
    }

    private async Task StopEmbeddedServerAsync()
    {
#if EMBED_HTTP_SERVER
        if (_embeddedApp == null) return;
        try
        {
            AppendLog("Stopping embedded HTTP server...");
            await _embeddedApp.StopAsync().ConfigureAwait(false);
            if (_embeddedApp is IAsyncDisposable adi) await adi.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { AppendLog($"Error stopping embedded server: {ex.Message}"); }
        finally { _embeddedApp = null; _embeddedAppTask = null; }
#else
        AppendLog("Embedded HTTP server is not enabled in this build.");
        await Task.CompletedTask;
#endif
    }
}