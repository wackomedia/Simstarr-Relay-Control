using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RelayTest.Properties
{
    public class EventForwarder : IDisposable
    {
        private readonly HttpClient _client;
        private readonly Uri _endpoint;
        private bool _disposed;

        // baseUrl example: "http://192.168.1.5:5000"
        public EventForwarder(string baseUrl, string token)
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrEmpty(token))
                _client.DefaultRequestHeaders.Add("X-Auth-Token", token);

            _endpoint = new Uri(baseUrl.TrimEnd('/') + "/api/relay");
        }

        public async Task<bool> SendFogAsync(int relayIndex, int durationMs)
        {
            var payload = new { action = "fog", relayIndex, durationMs, timestamp = DateTime.UtcNow };
            return await PostJsonAsync(payload).ConfigureAwait(false);
        }

        public async Task<bool> SendSetRelayAsync(int relayIndex, bool state)
        {
            var payload = new { action = "setRelay", relayIndex, state, timestamp = DateTime.UtcNow };
            return await PostJsonAsync(payload).ConfigureAwait(false);
        }

        // Lightweight ping to verify the relay endpoint accepts requests.
        public async Task<bool> PingAsync()
        {
            var payload = new { action = "ping", timestamp = DateTime.UtcNow };
            return await PostJsonAsync(payload).ConfigureAwait(false);
        }

        private async Task<bool> PostJsonAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            try
            {
                var resp = await _client.PostAsync(_endpoint, content).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _client.Dispose();
            _disposed = true;
        }
    }
}