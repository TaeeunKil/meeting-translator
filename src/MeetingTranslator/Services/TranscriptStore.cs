using Microsoft.Data.Sqlite;
using MeetingTranslator.Models;

namespace MeetingTranslator.Services;

public sealed class TranscriptStore
{
    private readonly string _connectionString;

    public TranscriptStore(string? databasePath = null)
    {
        databasePath ??= Path.Combine(AppSettings.DirectoryPath, "meetings.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS transcript_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                source TEXT NOT NULL,
                original_text TEXT NOT NULL,
                translated_text TEXT NOT NULL,
                confidence REAL NOT NULL,
                speaker_name TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_transcript_meeting_time
            ON transcript_entries(meeting_id, timestamp);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureSpeakerColumnAsync(connection);
        var columns = connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('transcript_entries') WHERE name='utterance_id'";
        if (Convert.ToInt32(await columns.ExecuteScalarAsync()) == 0)
        {
            var migration = connection.CreateCommand();
            migration.CommandText = "ALTER TABLE transcript_entries ADD COLUMN utterance_id INTEGER NOT NULL DEFAULT 0";
            await migration.ExecuteNonQueryAsync();
        }
        var index = connection.CreateCommand();
        index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_transcript_utterance ON transcript_entries(meeting_id,utterance_id) WHERE utterance_id>0";
        await index.ExecuteNonQueryAsync();
    }

    public async Task<TranscriptEntry> AddAsync(string meetingId, AudioSource source, string original,
        string translated, double confidence, string? speakerName = null, long utteranceId = 0)
    {
        var timestamp = DateTimeOffset.Now;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcript_entries
              (meeting_id, timestamp, source, original_text, translated_text, confidence, speaker_name, utterance_id)
            VALUES ($meeting, $timestamp, $source, $original, $translated, $confidence, $speaker, $utterance)
            ON CONFLICT(meeting_id,utterance_id) WHERE utterance_id>0 DO UPDATE SET
              original_text=excluded.original_text, translated_text=excluded.translated_text,
              confidence=excluded.confidence, speaker_name=excluded.speaker_name
            RETURNING id,timestamp;
            """;
        command.Parameters.AddWithValue("$meeting", meetingId);
        command.Parameters.AddWithValue("$timestamp", timestamp.ToString("O"));
        command.Parameters.AddWithValue("$source", source.ToString());
        command.Parameters.AddWithValue("$original", original);
        command.Parameters.AddWithValue("$translated", translated);
        command.Parameters.AddWithValue("$confidence", confidence);
        command.Parameters.AddWithValue("$speaker", (object?)speakerName ?? DBNull.Value);
        command.Parameters.AddWithValue("$utterance", utteranceId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Transcript save returned no row");
        var id = reader.GetInt64(0);
        timestamp = DateTimeOffset.Parse(reader.GetString(1));
        return new(id, meetingId, timestamp, source, original, translated, confidence, speakerName);
    }

    public async Task DeleteUtteranceAsync(string meetingId, long utteranceId)
    {
        if (utteranceId <= 0) throw new ArgumentOutOfRangeException(nameof(utteranceId));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transcript_entries WHERE meeting_id=$meeting AND utterance_id=$utterance";
        command.Parameters.AddWithValue("$meeting", meetingId);
        command.Parameters.AddWithValue("$utterance", utteranceId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<TranscriptEntry>> GetMeetingAsync(string meetingId)
    {
        var results = new List<TranscriptEntry>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, meeting_id, timestamp, source, original_text, translated_text, confidence, speaker_name
            FROM transcript_entries WHERE meeting_id = $meeting ORDER BY timestamp;
            """;
        command.Parameters.AddWithValue("$meeting", meetingId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new(
                reader.GetInt64(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)),
                Enum.Parse<AudioSource>(reader.GetString(3)), reader.GetString(4), reader.GetString(5),
                reader.GetDouble(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return results;
    }

    private static async Task EnsureSpeakerColumnAsync(SqliteConnection connection)
    {
        var schema = connection.CreateCommand();
        schema.CommandText = "PRAGMA table_info(transcript_entries);";
        var hasSpeakerColumn = false;

        await using (var reader = await schema.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), "speaker_name", StringComparison.OrdinalIgnoreCase))
                {
                    hasSpeakerColumn = true;
                    break;
                }
            }
        }

        if (hasSpeakerColumn)
            return;

        var migration = connection.CreateCommand();
        migration.CommandText =
            "ALTER TABLE transcript_entries ADD COLUMN speaker_name TEXT;";
        await migration.ExecuteNonQueryAsync();
    }
}
