using BenchmarkDotNet.Attributes;
using System;
using System.Text.Json;

namespace RelayTest.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class JournalWatcherBenchmark
    {
        private string _heatWarningLine = "";
        private string _heatDamageLine = "";
        private string _customEventLine = "";
        private string _noEventLine = "";
        [GlobalSetup]
        public void Setup()
        {
            // Simulate typical Elite Dangerous journal lines
            _heatWarningLine = """{"timestamp":"2024-01-08T12:34:56Z","event":"HeatWarning","hull":80}""";
            _heatDamageLine = """{"timestamp":"2024-01-08T12:34:56Z","event":"HeatDamage","hullHealth":0.65}""";
            _customEventLine = """{"timestamp":"2024-01-08T12:34:56Z","event":"FSD_Jump","StarSystem":"Colonia","StarPos":[9530.5,-910.84375,19808.125]}""";
            _noEventLine = """{"timestamp":"2024-01-08T12:34:56Z","Cargo":[{"ItemName":"Gold","Count":50}]}""";
        }

        [Benchmark]
        public void ParseHeatWarning()
        {
            using var doc = JsonDocument.Parse(_heatWarningLine);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
            {
                var eventName = ev.GetString();
            }
        }

        [Benchmark]
        public void ParseHeatDamage()
        {
            using var doc = JsonDocument.Parse(_heatDamageLine);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
            {
                var eventName = ev.GetString();
            }
        }

        [Benchmark]
        public void ParseCustomEvent()
        {
            using var doc = JsonDocument.Parse(_customEventLine);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
            {
                var eventName = ev.GetString();
            }
        }

        [Benchmark]
        public void ParseLineWithoutEvent()
        {
            using var doc = JsonDocument.Parse(_noEventLine);
            var root = doc.RootElement;
            if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
            {
                var eventName = ev.GetString();
            }
        }

        [Benchmark]
        public void EventNameComparison()
        {
            var eventName = "HeatWarning";
            var result1 = string.Equals(eventName, "HeatWarning", StringComparison.OrdinalIgnoreCase);
            var result2 = string.Equals(eventName, "HeatDamage", StringComparison.OrdinalIgnoreCase);
            var result3 = string.Equals(eventName, "CustomEvent", StringComparison.OrdinalIgnoreCase);
        }

        [Benchmark]
        public void EventNameComparisonOrdinal()
        {
            var eventName = "HeatWarning";
            var result1 = eventName.Equals("HeatWarning", StringComparison.Ordinal);
            var result2 = eventName.Equals("HeatDamage", StringComparison.Ordinal);
            var result3 = eventName.Equals("CustomEvent", StringComparison.Ordinal);
        }
    }
}