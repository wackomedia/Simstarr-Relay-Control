using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RelayTest.Properties;

class JournalWatcher : IDisposable
{
    private readonly string _rootDir;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action? HeatWarning;
    public event Action? HeatDamage;
    public event Action<int>? CustomRelayEvent; // New: per-relay event with relay index
    public event Action<string>? DebugLine; // for UI logging

    public JournalWatcher(string rootDir)
    {
        _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => TailLoop(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        try { if (_task != null) await _task.ConfigureAwait(false); } catch { }
        _cts.Dispose();
        _cts = null;
        _task = null;
    }

    private async Task TailLoop(CancellationToken ct)
    {
        string? currentFile = null;
        StreamReader? reader = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    string? latest = GetLatestJournalFile(_rootDir);
                    if (latest == null)
                    {
                        DebugLine?.Invoke($"No journal files found under {_rootDir}");
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (reader == null || !string.Equals(latest, currentFile, StringComparison.OrdinalIgnoreCase))
                    {
                        reader?.Dispose();
                        currentFile = latest;
                        var fs = new FileStream(currentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        reader = new StreamReader(fs);
                        reader.BaseStream.Seek(0, SeekOrigin.End);
                        reader.DiscardBufferedData();
                        DebugLine?.Invoke($"Tailing {currentFile}");
                    }

                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        await Task.Delay(150, ct).ConfigureAwait(false);
                        continue;
                    }

                    // Parse JSON and check event property
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        if (TryGetEventName(doc.RootElement, out string? ev))
                        {
                            DebugLine?.Invoke($"Event: {ev}");
                            
                            // Check against hardcoded heat events
                            if (string.Equals(ev, "HeatWarning", StringComparison.OrdinalIgnoreCase))
                                HeatWarning?.Invoke();
                            else if (string.Equals(ev, "HeatDamage", StringComparison.OrdinalIgnoreCase))
                                HeatDamage?.Invoke();
                            else
                            {
                                // Check against configured relay event names
                                var relayEventTypes = Settings.Default.RelayEventTypes;
                                if (relayEventTypes != null)
                                {
                                    for (int i = 0; i < relayEventTypes.Length && i < 4; i++)
                                    {
                                        if (!string.IsNullOrEmpty(relayEventTypes[i]) && 
                                            string.Equals(ev, relayEventTypes[i], StringComparison.OrdinalIgnoreCase))
                                        {
                                            // Relay i was configured with this event name
                                            DebugLine?.Invoke($"Custom event matched: Relay {i + 1} -> {ev}");
                                            CustomRelayEvent?.Invoke(i); // Fire with specific relay index
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Enhanced debug: show first 200 chars of the line and available properties
                            var root = doc.RootElement;
                            var props = string.Join(", ", root.EnumerateObject().Select(p => p.Name).Take(10));
                            DebugLine?.Invoke($"[NO EVENT] Props: {props} | Line start: {line.Substring(0, Math.Min(200, line.Length))}");
                        }
                    }
                    catch (JsonException ex)
                    {
                        DebugLine?.Invoke($"Invalid JSON line: {ex.Message}");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    DebugLine?.Invoke($"Watcher error: {ex.Message}");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            reader?.Dispose();
        }
    }

    private static string? GetLatestJournalFile(string journalDir)
    {
        try
        {
            var files = Directory.EnumerateFiles(journalDir, "Journal.*.log", SearchOption.AllDirectories).ToList();
            if (files.Count == 0) return null;
            return files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetEventName(JsonElement el, out string? eventName)
    {
        eventName = null;
        if (el.ValueKind != JsonValueKind.Object) return false;
        if (el.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
        {
            eventName = ev.GetString();
            return !string.IsNullOrEmpty(eventName);
        }
        return false;
    }

    public void Dispose()
    {
        var _ = StopAsync();
    }
}