using System.Text.Json;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public sealed class MonthlyUsageGuard
{
    private sealed class Usage
    {
        public string Month { get; set; } = "";
        public int Characters { get; set; }
    }

    private readonly object _gate = new();
    private readonly string _path = Path.Combine(AppSettings.DirectoryPath, "usage.json");
    private Usage _usage;

    public MonthlyUsageGuard()
    {
        Directory.CreateDirectory(AppSettings.DirectoryPath);
        _usage = File.Exists(_path)
            ? JsonSerializer.Deserialize<Usage>(File.ReadAllText(_path)) ?? new()
            : new();
        ResetIfNewMonth();
    }

    public int CharactersUsed { get { lock (_gate) return _usage.Characters; } }

    public bool TryConsume(int characters, int limit)
    {
        lock (_gate)
        {
            ResetIfNewMonth();
            if (_usage.Characters + characters > limit) return false;
            _usage.Characters += characters;
            File.WriteAllText(_path, JsonSerializer.Serialize(_usage));
            return true;
        }
    }

    private void ResetIfNewMonth()
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        if (_usage.Month == month) return;
        _usage = new Usage { Month = month };
        File.WriteAllText(_path, JsonSerializer.Serialize(_usage));
    }
}
