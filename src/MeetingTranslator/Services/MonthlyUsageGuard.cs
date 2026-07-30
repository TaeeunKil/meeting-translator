using System.Text;
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
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private Usage _usage;

    public MonthlyUsageGuard(
        string? path = null,
        TimeProvider? timeProvider = null)
    {
        _path = path ?? Path.Combine(AppSettings.DirectoryPath, "usage.json");
        _timeProvider = timeProvider ?? TimeProvider.System;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _usage = LoadUsage();
        ResetIfNewMonth();
    }

    public int CharactersUsed
    {
        get
        {
            lock (_gate)
            {
                ResetIfNewMonth();
                return _usage.Characters;
            }
        }
    }

    public bool TryReserve(int characters, int limit)
    {
        if (characters < 0)
            throw new ArgumentOutOfRangeException(nameof(characters));
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        lock (_gate)
        {
            ResetIfNewMonth();
            if (_usage.Characters + characters > limit)
                return false;

            _usage.Characters += characters;
            SaveUsage();
            return true;
        }
    }

    public void Release(int characters)
    {
        if (characters <= 0)
            return;

        lock (_gate)
        {
            ResetIfNewMonth();
            _usage.Characters = Math.Max(0, _usage.Characters - characters);
            SaveUsage();
        }
    }

    public static int CountBillableCharacters(string text) =>
        text.EnumerateRunes().Count();

    private Usage LoadUsage()
    {
        if (!File.Exists(_path))
            return new Usage();

        try
        {
            return JsonSerializer.Deserialize<Usage>(
                File.ReadAllText(_path)) ?? new Usage();
        }
        catch (JsonException)
        {
            return new Usage();
        }
    }

    private void ResetIfNewMonth()
    {
        var month = _timeProvider.GetUtcNow().ToString("yyyy-MM");
        if (_usage.Month == month)
            return;

        _usage = new Usage { Month = month };
        SaveUsage();
    }

    private void SaveUsage() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_usage));
}
