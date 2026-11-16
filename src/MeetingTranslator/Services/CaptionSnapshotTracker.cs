using System.Text.RegularExpressions;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

internal sealed class CaptionSnapshotTracker
{
    internal sealed record Update(CaptionSegment Segment, bool Final);
    private sealed record Span(long Id, int Start, int End, string Prefix = "");
    private readonly List<Span> _spans = [];
    private string _previous = "";
    private long _nextId = 1;

    // Windows exposes a mutable text snapshot, not stable utterance IDs.
    // Map edits to existing ranges rather than identifying speech by its wording.
    public IReadOnlyList<Update> Accept(string snapshot)
    {
        snapshot = Regex.Replace(snapshot, @"\s+", " ").Trim();
        if (snapshot.Length == 0 || snapshot == _previous)
            return [];
        var before = _spans.Select((s, i) => (s.Id, Text: Text(s, _previous), Final: i < _spans.Count - 1))
            .ToDictionary(s => s.Id);
        var output = new List<Update>();
        if (_previous.Length == 0)
        {
            // Do not import old captions already on screen when capture starts.
            var matches = Regex.Matches(snapshot, @"[^.!?。！？]+[.!?。！？]?");
            var start = matches.Count == 0 ? 0 : matches[^1].Index;
            _spans.Add(new Span(_nextId++, start, snapshot.Length));
        }
        else
        {
            var overlap = SuffixPrefixOverlap(_previous, snapshot);
            var retainedStart = _previous.Length - overlap;
            var retainedUtterance = overlap > 0 && _spans.Any(span =>
                retainedStart >= span.Start && retainedStart < span.End &&
                string.IsNullOrWhiteSpace(_previous[span.Start..retainedStart]));
            if ((overlap >= 24 || retainedUtterance) && overlap < _previous.Length && !snapshot.StartsWith(_previous, StringComparison.Ordinal))
            {
                var trim = _previous.Length - overlap;
                var activeId = _spans.LastOrDefault()?.Id;
                foreach (var span in _spans.ToArray())
                {
                    _spans.Remove(span);
                    if (span.End <= trim)
                    {
                        // Off-screen history stays in the transcript/database.
                        if (span.Id == activeId)
                            output.Add(new Update(new CaptionSegment(Text(span, _previous), UtteranceId: span.Id), true));
                        continue;
                    }
                    var prefix = span.Prefix + (span.Start < trim ? _previous[span.Start..trim] : "");
                    _spans.Add(span with {Start = Math.Max(0, span.Start - trim), End = span.End - trim, Prefix = prefix});
                }
                _spans.Sort((a, b) => a.Start.CompareTo(b.Start));
                _previous = _previous[trim..];
            }

            if (_spans.Count == 0)
                _spans.Add(new Span(_nextId++, snapshot.StartsWith(_previous, StringComparison.Ordinal) ? _previous.Length : 0, snapshot.Length));
            else if (snapshot.StartsWith(_previous, StringComparison.Ordinal))
            {
                var last = _spans[^1];
                if (EndsSentence(Text(last, _previous)))
                    _spans.Add(new Span(_nextId++, _previous.Length, snapshot.Length));
                else
                    _spans[^1] = last with {End = snapshot.Length};
            }
            else
            {
                var prefix = 0;
                while (prefix < Math.Min(_previous.Length, snapshot.Length) && _previous[prefix] == snapshot[prefix])
                    prefix++;
                var suffix = 0;
                while (suffix < Math.Min(_previous.Length, snapshot.Length) - prefix &&
                       _previous[^(suffix + 1)] == snapshot[^(suffix + 1)])
                    suffix++;
                var oldEnd = _previous.Length - suffix;
                var newEnd = snapshot.Length - suffix;
                var delta = snapshot.Length - _previous.Length;
                var affected = new List<Span>();
                for (var i = 0; i < _spans.Count; i++)
                {
                    var span = _spans[i];
                    if (span.End <= prefix)
                        continue;
                    if (span.Start >= oldEnd && span.Start > prefix)
                        _spans[i] = span with {Start = span.Start + delta, End = span.End + delta};
                    else
                        affected.Add(span);
                }
                if (affected.Count > 0)
                {
                    var first = affected[0];
                    var last = affected[^1];
                    var start = Math.Min(first.Start, prefix);
                    var end = last.End >= oldEnd ? last.End + delta : newEnd;
                    var index = _spans.IndexOf(first);
                    foreach (var span in affected)
                        _spans.Remove(span);
                    _spans.Insert(index, first with {Start = start, End = Math.Max(start, end)});
                    foreach (var removed in affected.Skip(1))
                        Remove(output, removed.Id);
                }
                foreach (var empty in _spans.Where(span => Text(span, snapshot).Length == 0).ToArray())
                {
                    _spans.Remove(empty);
                    Remove(output, empty.Id);
                }
                // Removing a provisional full stop joins the next range too.
                for (var i = 0; i + 1 < _spans.Count;)
                {
                    if (!EndsSentence(Text(_spans[i], snapshot)))
                    {
                        var removed = _spans[i + 1];
                        _spans[i] = _spans[i] with {End = removed.End};
                        _spans.RemoveAt(i + 1);
                        Remove(output, removed.Id);
                    }
                    else i++;
                }
            }
        }
        _previous = snapshot;
        for (var i = 0; i < _spans.Count; i++)
        {
            var span = _spans[i];
            var text = Text(span, snapshot);
            var final = i < _spans.Count - 1;
            if (before.TryGetValue(span.Id, out var old) && old.Text == text && old.Final == final)
                continue;
            output.Add(new Update(new CaptionSegment(text, UtteranceId: span.Id), final));
        }
        return output;
    }

    public CaptionSegment? Flush() => _spans.Count == 0 ? null :
        new CaptionSegment(Text(_spans[^1], _previous), UtteranceId: _spans[^1].Id);

    private static string Text(Span span, string snapshot) =>
        (span.Prefix + snapshot[Math.Clamp(span.Start, 0, snapshot.Length)..Math.Clamp(span.End, 0, snapshot.Length)]).Trim();
    private static bool EndsSentence(string text) => text.Length > 0 && ".!?。！？".Contains(text[^1]);
    private static void Remove(List<Update> updates, long id) =>
        updates.Add(new Update(new CaptionSegment("", UtteranceId: id, IsRemoved: true), true));

    private static int SuffixPrefixOverlap(string previous, string current)
    {
        var joined = current + '\0' + previous;
        var lengths = new int[joined.Length];
        for (var i = 1; i < joined.Length; i++)
        {
            var length = lengths[i - 1];
            while (length > 0 && joined[i] != joined[length]) length = lengths[length - 1];
            if (joined[i] == joined[length]) length++;
            lengths[i] = length;
        }
        return Math.Min(lengths[^1], current.Length);
    }
}
