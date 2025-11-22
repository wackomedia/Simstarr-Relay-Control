using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FTD2XX_NET;

class RelayController : IDisposable
{
    private readonly FTDI _device = new FTDI();
    private byte _currentMask = 0x00; // Track relay states
    private readonly object _sync = new object();
    private bool _initialized = false;

    // Track pulse tasks and cancellation so we can stop them on Close/Dispose
    private CancellationTokenSource? _pulseCts;
    private readonly List<Task> _activePulses = new List<Task>();

    public bool Init()
    {
        lock (_sync)
        {
            var status = _device.OpenByIndex(0);
            if (status != FTDI.FT_STATUS.FT_OK)
                return false;

            status = _device.SetBitMode(0x0F, 0x01);
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                _device.Close();
                return false;
            }

            _initialized = true;
            _pulseCts = new CancellationTokenSource();
            return true;
        }
    }

    public void SetRelay(int relayIndex, bool on)
    {
        if (relayIndex < 0 || relayIndex > 3) // validate index
            return;

        lock (_sync)
        {
            if (!_initialized) return;

            if (on)
                _currentMask |= (byte)(1 << relayIndex);
            else
                _currentMask &= (byte)~(1 << relayIndex);

            uint bytesWritten = 0;
            _device.Write(new byte[] { _currentMask }, 1u, ref bytesWritten);
        }
    }

    public void PulseRelay(int relayIndex, int durationMs)
    {
        // Make pulse cancellable and track the task so Close can stop it.
        var cts = _pulseCts;
        if (cts == null || cts.IsCancellationRequested) return;

        var token = cts.Token;

        Task t = Task.Run(async () =>
        {
            try
            {
                // Turn on the requested relay only
                SetRelay(relayIndex, true);

                // Wait for duration or cancellation
                await Task.Delay(durationMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // cancelled — fallthrough to ensure we turn relay off
            }
            catch
            {
                // ignore other exceptions here
            }
            finally
            {
                // Ensure relay is turned off (best effort)
                try { SetRelay(relayIndex, false); } catch { }
            }
        }, token);

        lock (_activePulses)
        {
            _activePulses.Add(t);
            // clean up completed tasks occasionally
            _activePulses.RemoveAll(x => x.IsCompleted);
        }
    }

    // Default FogBlast targets relay index 0 (relay 1). Overload provided for explicit relay index.
    public void FogBlast(int durationMs = 1500)
    {
        FogBlast(0, durationMs);
    }

    public void FogBlast(int relayIndex, int durationMs)
    {
        PulseRelay(relayIndex, durationMs);
    }

    public void Close()
    {
        lock (_sync)
        {
            if (!_initialized) return;

            // Cancel all pulses and wait a short time for them to finish
            try
            {
                _pulseCts?.Cancel();
                Task[] tasks;
                lock (_activePulses) { tasks = _activePulses.ToArray(); }
                if (tasks.Length > 0)
                {
                    try { Task.WaitAll(tasks, 2000); } catch { /* ignore timeouts */ }
                }
            }
            catch { /* ignore */ }

            // Ensure all relays are off (single write)
            try
            {
                _currentMask = 0x00;
                uint bytesWritten = 0;
                _device.Write(new byte[] { 0x00 }, 1u, ref bytesWritten);
            }
            catch { }

            // Close device
            try { _device.Close(); } catch { }
            _initialized = false;

            // Dispose pulse CTS
            try { _pulseCts?.Dispose(); } catch { }
            _pulseCts = null;

            lock (_activePulses) { _activePulses.Clear(); }
        }
    }

    public void Dispose()
    {
        Close();
    }
}