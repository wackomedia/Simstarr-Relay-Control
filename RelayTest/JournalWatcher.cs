using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class JournalWatcher : IDisposable
{
    private readonly string _rootDir;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action? HeatWarning;
    public event Action? HeatDamage;
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
                            if (string.Equals(ev, "HeatWarning", StringComparison.OrdinalIgnoreCase))
                                HeatWarning?.Invoke();
                            else if (string.Equals(ev, "HeatDamage", StringComparison.OrdinalIgnoreCase))
                                HeatDamage?.Invoke();
                        }
                        else
                        {
                            // optional: emit debug
                            DebugLine?.Invoke("No event name found in line.");
                        }
                    }
                    catch (JsonException)
                    {
                        DebugLine?.Invoke("Invalid JSON line.");
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