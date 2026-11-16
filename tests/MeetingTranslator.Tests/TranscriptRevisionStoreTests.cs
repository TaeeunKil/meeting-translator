using MeetingTranslator.Models;
using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class TranscriptRevisionStoreTests
{
    [Fact]
    public async Task SnapshotRetractions_RemoveSavedDuplicatesAndKeepOffscreenHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var store = new TranscriptStore(path);
            await store.InitializeAsync();
            var tracker = new CaptionSnapshotTracker();
            foreach (var snapshot in new[] {"Alpha.", "Alpha. Beta.", "Alpha. Beta. Gamma.", "Alpha."})
                foreach (var update in tracker.Accept(snapshot))
                    if (update.Segment.IsRemoved)
                        await store.DeleteUtteranceAsync("meeting", update.Segment.UtteranceId);
                    else if (update.Final)
                        await store.AddAsync("meeting", AudioSource.SystemAudio, update.Segment.Text, "translation", 1, utteranceId: update.Segment.UtteranceId);
            var last = tracker.Flush()!;
            await store.AddAsync("meeting", AudioSource.SystemAudio, last.Text, "translation", 1, utteranceId: last.UtteranceId);
            Assert.Equal("Alpha.", Assert.Single(await store.GetMeetingAsync("meeting")).OriginalText);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task Initialize_MigratesLegacyRowsWithoutChangingTheirContent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE transcript_entries(id INTEGER PRIMARY KEY AUTOINCREMENT,meeting_id TEXT NOT NULL,timestamp TEXT NOT NULL,source TEXT NOT NULL,original_text TEXT NOT NULL,translated_text TEXT NOT NULL,confidence REAL NOT NULL);
                    INSERT INTO transcript_entries(meeting_id,timestamp,source,original_text,translated_text,confidence) VALUES('old','2026-11-17T00:00:00+00:00','SystemAudio','historic source','historic translation',1);
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var store = new TranscriptStore(path);
            await store.InitializeAsync();
            await store.InitializeAsync();
            var entry = Assert.Single(await store.GetMeetingAsync("old"));
            Assert.Equal("historic source", entry.OriginalText);
            Assert.Equal("historic translation", entry.TranslatedText);
            await store.AddAsync("old", AudioSource.SystemAudio, "new", "new translation", 1, utteranceId: 1);
            Assert.Equal(2, (await store.GetMeetingAsync("old")).Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task RevisedFinal_UpdatesTheExistingEntryAndKeepsItsTimestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var store = new TranscriptStore(path);
            await store.InitializeAsync();
            var first = await store.AddAsync("meeting", AudioSource.SystemAudio, "This is a.", "초안", 1, utteranceId: 1);
            var revised = await store.AddAsync("meeting", AudioSource.SystemAudio, "This is a better design.", "수정 번역", 1, utteranceId: 1);
            var rows = await store.GetMeetingAsync("meeting");
            Assert.Single(rows);
            Assert.Equal(first.Id, revised.Id);
            Assert.Equal(first.Timestamp, revised.Timestamp);
            Assert.Equal("수정 번역", rows[0].TranslatedText);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task MergedUtterance_DeletesOnlyTheSupersededRowInTheSameMeeting()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var store = new TranscriptStore(path);
            await store.InitializeAsync();
            await store.AddAsync("meeting", AudioSource.SystemAudio, "This is a.", "첫째", 1, utteranceId: 1);
            await store.AddAsync("meeting", AudioSource.SystemAudio, "Better design", "둘째", 1, utteranceId: 2);
            await store.AddAsync("another", AudioSource.SystemAudio, "Unrelated meeting", "별도", 1, utteranceId: 2);
            await store.AddAsync("meeting", AudioSource.SystemAudio, "Historic entry", "기존", 1);
            await store.DeleteUtteranceAsync("meeting", 2);
            await store.AddAsync("meeting", AudioSource.SystemAudio, "This is a better design.", "합침", 1, utteranceId: 1);
            Assert.Equal(2, (await store.GetMeetingAsync("meeting")).Count);
            Assert.Single(await store.GetMeetingAsync("another"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.DeleteUtteranceAsync("meeting", 0));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
