using System.Text;

namespace DeltaHarmonica.Core;

public sealed record MidiTrackScore(string Name, Score Score);

/// <summary>Reads standard MIDI format 0/1 and exposes each melodic track separately.</summary>
public static class MidiImporter
{
    private sealed record RawNote(long Start, long End, int Pitch, int Channel);
    private sealed record RawTrack(string Name, List<RawNote> Notes);

    public static IReadOnlyList<MidiTrackScore> ReadTracks(string path, int minMidi, int maxMidi)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (ReadAscii(reader, 4) != "MThd") throw new InvalidDataException("不是有效的 MIDI 文件。");
        var headerLength = ReadBigEndian(reader, 4);
        if (headerLength < 6 || headerLength > 1024) throw new InvalidDataException("MIDI 文件头无效。");
        var format = ReadBigEndian(reader, 2);
        var trackCount = ReadBigEndian(reader, 2);
        var division = ReadBigEndian(reader, 2);
        if (format > 1 || trackCount is < 1 or > 256 || (division & 0x8000) != 0 || division == 0)
            throw new InvalidDataException("仅支持标准 MIDI 格式 0/1 的节拍时间轴。");
        stream.Position += headerLength - 6;

        var rawTracks = new List<RawTrack>();
        var tempos = new List<(long Tick, int Microseconds)> { (0, 500000) };
        for (var index = 0; index < trackCount; index++)
        {
            if (ReadAscii(reader, 4) != "MTrk") throw new InvalidDataException("MIDI 音轨数据无效。");
            var length = ReadBigEndian(reader, 4);
            if (length < 0 || length > stream.Length - stream.Position || length > 100_000_000)
                throw new InvalidDataException("MIDI 音轨长度无效。");
            rawTracks.Add(ParseTrack(reader.ReadBytes(length), index + 1, tempos));
        }

        var tempoMap = tempos.OrderBy(t => t.Tick).ToArray();
        var output = new List<MidiTrackScore>();
        foreach (var track in rawTracks)
        {
            var notes = track.Notes.Where(n => n.Channel != 9 && n.Pitch >= minMidi && n.Pitch <= maxMidi && n.End > n.Start)
                .Select(n => new NoteEvent(ToMs(n.Start), n.Pitch, Math.Max(1, ToMs(n.End) - ToMs(n.Start))))
                .Where(n => n.DurationMs >= 70)
                .OrderBy(n => n.StartMs).ThenByDescending(n => n.Midi).ToList();
            if (notes.Count == 0) continue;

            // One keyboard melody at a time: for simultaneous notes, retain the
            // highest pitch; for staggered chords, release the preceding note.
            var melody = new Score { Title = Path.GetFileNameWithoutExtension(path) + " - " + track.Name };
            foreach (var note in notes)
            {
                if (melody.Notes.Count > 0)
                {
                    var previous = melody.Notes[^1];
                    if (note.StartMs - previous.StartMs < 15) continue;
                    if (note.StartMs < previous.EndMs)
                        melody.Notes[^1] = previous with { DurationMs = note.StartMs - previous.StartMs };
                    if (melody.Notes[^1].DurationMs < 60) melody.Notes.RemoveAt(melody.Notes.Count - 1);
                }
                melody.Notes.Add(note);
            }
            melody.Validate();
            output.Add(new MidiTrackScore(track.Name, melody));
        }
        if (output.Count == 0) throw new InvalidDataException("该 MIDI 文件在指定音域内没有可用的旋律音符。");
        return output;

        int ToMs(long tick)
        {
            var microseconds = 0.0;
            var previousTick = 0L;
            var tempo = 500000;
            foreach (var change in tempoMap)
            {
                if (change.Tick > tick) break;
                if (change.Tick > previousTick)
                    microseconds += (change.Tick - previousTick) * (double)tempo / division;
                previousTick = change.Tick;
                tempo = change.Microseconds;
            }
            microseconds += (tick - previousTick) * (double)tempo / division;
            return checked((int)Math.Round(microseconds / 1000));
        }
    }

    private static RawTrack ParseTrack(byte[] data, int number, List<(long Tick, int Microseconds)> tempos)
    {
        var position = 0;
        var tick = 0L;
        var runningStatus = 0;
        var name = $"音轨 {number}";
        var notes = new List<RawNote>();
        var active = new Dictionary<(int Channel, int Pitch), Queue<long>>();
        while (position < data.Length)
        {
            tick = checked(tick + ReadVariableLength(data, ref position));
            if (position >= data.Length) throw new InvalidDataException("MIDI 事件不完整。");
            var status = data[position] >= 0x80 ? data[position++] : runningStatus;
            if (status == 0) throw new InvalidDataException("MIDI 运行状态无效。");
            if (status == 0xFF)
            {
                runningStatus = 0;
                var type = ReadByte(data, ref position);
                var length = ReadVariableLength(data, ref position);
                if (length > data.Length - position) throw new InvalidDataException("MIDI 元事件不完整。");
                if (type == 0x03 && length > 0)
                    name = Encoding.UTF8.GetString(data, position, length).Trim('\0', ' ');
                if (type == 0x51 && length == 3)
                {
                    var tempo = (data[position] << 16) | (data[position + 1] << 8) | data[position + 2];
                    if (tempo > 0) tempos.Add((tick, tempo));
                }
                position += length;
                if (type == 0x2F) break;
                continue;
            }
            if (status is 0xF0 or 0xF7)
            {
                runningStatus = 0;
                var length = ReadVariableLength(data, ref position);
                if (length > data.Length - position) throw new InvalidDataException("MIDI SysEx 事件不完整。");
                position += length;
                continue;
            }
            if (status >= 0xF0) throw new InvalidDataException("MIDI 事件类型不受支持。");
            runningStatus = status;
            var channel = status & 0x0F;
            var kind = status & 0xF0;
            var pitch = ReadByte(data, ref position);
            var velocity = kind is 0xC0 or 0xD0 ? 0 : ReadByte(data, ref position);
            if (kind == 0x90 && velocity > 0)
            {
                var key = (channel, pitch);
                if (!active.TryGetValue(key, out var starts)) active[key] = starts = new Queue<long>();
                starts.Enqueue(tick);
            }
            else if (kind == 0x80 || kind == 0x90)
            {
                var key = (channel, pitch);
                if (active.TryGetValue(key, out var starts) && starts.Count > 0)
                    notes.Add(new RawNote(starts.Dequeue(), tick, pitch, channel));
            }
        }
        return new RawTrack(string.IsNullOrWhiteSpace(name) ? $"音轨 {number}" : name, notes);
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("MIDI 文件已截断。");
        return Encoding.ASCII.GetString(bytes);
    }

    private static int ReadBigEndian(BinaryReader reader, int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++) value = (value << 8) | reader.ReadByte();
        return value;
    }

    private static int ReadByte(byte[] data, ref int position)
    {
        if (position >= data.Length) throw new InvalidDataException("MIDI 事件不完整。");
        return data[position++];
    }

    private static int ReadVariableLength(byte[] data, ref int position)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var next = ReadByte(data, ref position);
            value = (value << 7) | (next & 0x7F);
            if ((next & 0x80) == 0) return value;
        }
        throw new InvalidDataException("MIDI 时间值无效。");
    }
}
