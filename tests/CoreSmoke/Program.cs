using DeltaHarmonica.Core;

var source = new Score { Title = "测试" };
source.Notes.AddRange([new(0, 60, 200), new(300, 61, 250), new(600, 72, 300)]);
var temp = Path.GetTempFileName();
ScoreFile.Save(temp, source);
var loaded = ScoreFile.Load(temp);
File.Delete(temp);
if (loaded.Notes.Count != 3 || loaded.Title != "测试") throw new Exception("曲谱读写失败");
for (var midi = 48; midi <= 85; midi++)
    if (!NoteMapper.TryMap(midi, 60, out _)) throw new Exception($"音高 {midi} 缺少按键映射");
Console.WriteLine("曲谱读写和全部 38 个半音映射通过");

var midiPath = Path.Combine(Path.GetTempPath(), $"delta-smoke-{Guid.NewGuid():N}.mid");
try
{
    MidiScoreFile.Save(midiPath, source);
    var midiBytes = File.ReadAllBytes(midiPath);
    if (System.Text.Encoding.ASCII.GetString(midiBytes, 0, 4) != "MThd"
        || System.Text.Encoding.ASCII.GetString(midiBytes, 14, 4) != "MTrk"
        || !midiBytes.AsSpan().Contains((byte)0x90)
        || !midiBytes.AsSpan().Contains((byte)0x80))
        throw new Exception("MIDI 文件结构或音符事件错误");
    var imported = MidiImporter.ReadTracks(midiPath, 48, 84);
    if (imported.Count != 1 || !imported[0].Score.Notes.Select(n => n.Midi).SequenceEqual(source.Notes.Select(n => n.Midi))
        || imported[0].Score.Notes.Any(n => Math.Abs(n.StartMs - source.Notes[imported[0].Score.Notes.IndexOf(n)].StartMs) > 2))
        throw new Exception("MIDI 导入未保留音高或时间");
}
finally { File.Delete(midiPath); }

// Format 1 puts the tempo map on a separate track. The second quarter note
// must be twice as long after the change from 120 to 60 BPM.
var multitrackPath = Path.Combine(Path.GetTempPath(), $"delta-tempo-{Guid.NewGuid():N}.mid");
try
{
    var header = new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 1, 0, 2, 1, 0xE0 };
    var tempo = new byte[] { 0, 0xFF, 0x51, 3, 7, 0xA1, 0x20, 0x83, 0x60, 0xFF, 0x51, 3, 0x0F, 0x42, 0x40, 0, 0xFF, 0x2F, 0 };
    var music = new byte[] { 0, 0x90, 60, 90, 0x83, 0x60, 0x80, 60, 0, 0, 0x90, 62, 90, 0x83, 0x60, 0x80, 62, 0, 0, 0xFF, 0x2F, 0 };
    using (var file = File.Create(multitrackPath))
    {
        file.Write(header);
        WriteTrack(file, tempo);
        WriteTrack(file, music);
    }
    var tracks = MidiImporter.ReadTracks(multitrackPath, 48, 84);
    if (tracks.Count != 1 || tracks[0].Score.Notes.Count != 2
        || tracks[0].Score.Notes[0].DurationMs != 500
        || tracks[0].Score.Notes[1].StartMs != 500
        || tracks[0].Score.Notes[1].DurationMs != 1000)
        throw new Exception("多音轨 MIDI 速度变化解析错误");
}
finally { File.Delete(multitrackPath); }

var lowMelody = new Score { Title = "低音导入" };
lowMelody.Notes.AddRange([new(21667, 52, 277), new(21944, 57, 278), new(22222, 67, 278)]);
var playable = MidiSongConverter.ToPlayableScore(lowMelody, 60, true);
if (playable.Notes.Count != 3 || playable.Notes[0].StartMs != 0
    || !playable.Notes.Select(n => n.Midi).SequenceEqual([52, 57, 67]))
    throw new Exception("MIDI 低音导入或开头空白裁剪失败");
Console.WriteLine("MIDI 导出、跨音轨速度和低音导入通过");

void WriteTrack(Stream file, byte[] data)
{
    file.Write(System.Text.Encoding.ASCII.GetBytes("MTrk"));
    file.WriteByte((byte)(data.Length >> 24));
    file.WriteByte((byte)(data.Length >> 16));
    file.WriteByte((byte)(data.Length >> 8));
    file.WriteByte((byte)data.Length);
    file.Write(data);
}

if (args.Length > 0 && args[0] == "--midi")
{
    var tracks = MidiImporter.ReadTracks(args[1], 0, 127);
    for (var i = 0; i < tracks.Count; i++)
    {
        var track = tracks[i];
        Console.WriteLine($"{i}: {track.Name}; {track.Score.Notes.Count} notes; range {track.Score.Notes.Min(n => n.Midi)}-{track.Score.Notes.Max(n => n.Midi)}; end {track.Score.Notes.Max(n => n.EndMs)}ms");
        foreach (var note in track.Score.Notes.Take(12)) Console.WriteLine($"  {note.StartMs},{note.Midi},{note.DurationMs}");
    }
    if (args.Length > 2)
    {
        var score = MidiSongConverter.ToPlayableScore(tracks[0].Score, 60, true);
        score.Title = Path.GetFileNameWithoutExtension(args[1]);
        ScoreFile.Save(args[2], score);
        var importedScore = ScoreFile.Load(args[2]);
        if (importedScore.Notes.Count != score.Notes.Count) throw new Exception("导出曲谱读回失败");
        Console.WriteLine($"已保存 {args[2]}，共 {importedScore.Notes.Count} 个音符，首音 {importedScore.Notes[0].StartMs}ms");
    }
}
