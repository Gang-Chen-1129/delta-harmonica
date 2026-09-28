using System.Globalization;
using System.Text;

namespace DeltaHarmonica.Core;

public sealed record NoteEvent(int StartMs, int Midi, int DurationMs)
{
    public int EndMs => checked(StartMs + DurationMs);
}

public sealed class Score
{
    public string Title { get; set; } = "未命名曲目";
    public List<NoteEvent> Notes { get; } = [];

    public void Validate()
    {
        if (Notes.Count == 0) throw new InvalidDataException("曲谱没有音符。");
        var previousEnd = 0;
        foreach (var (note, index) in Notes.OrderBy(n => n.StartMs).Select((n, i) => (n, i)))
        {
            if (note.StartMs < 0 || note.DurationMs <= 0 || note.Midi is < 0 or > 127)
                throw new InvalidDataException($"第 {index + 1} 个音符的时间或音高无效。");
            if (note.StartMs < previousEnd)
                throw new InvalidDataException($"第 {index + 1} 个音符与前一个重叠；目前仅支持单旋律。");
            previousEnd = note.EndMs;
        }
    }
}

public static class ScoreFile
{
    public static Score Load(string path)
    {
        var score = new Score { Title = Path.GetFileNameWithoutExtension(path) };
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("# title=", StringComparison.OrdinalIgnoreCase))
            {
                score.Title = trimmed[8..].Trim();
                continue;
            }
            if (trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split(',');
            if (parts.Length != 3 || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start)
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var midi)
                || !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration))
                throw new InvalidDataException($"{Path.GetFileName(path)} 第 {lineNumber} 行格式错误；应为：开始毫秒,MIDI音高,时长毫秒。");
            score.Notes.Add(new NoteEvent(start, midi, duration));
        }
        score.Notes.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        score.Validate();
        return score;
    }

    public static void Save(string path, Score score)
    {
        score.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder();
        builder.AppendLine("# delta-harmonica v1");
        builder.AppendLine($"# title={score.Title.Replace('\r', ' ').Replace('\n', ' ')}");
        builder.AppendLine("# start_ms,midi,duration_ms");
        foreach (var note in score.Notes.OrderBy(n => n.StartMs))
            builder.AppendLine(FormattableString.Invariant($"{note.StartMs},{note.Midi},{note.DurationMs}"));
        var temp = path + ".tmp";
        File.WriteAllText(temp, builder.ToString(), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
