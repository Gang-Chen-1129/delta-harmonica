using System.Text;

namespace DeltaHarmonica.Core;

/// <summary>Write a standard, single-track MIDI file from the detected melody.</summary>
public static class MidiScoreFile
{
    // 480 ticks per quarter note, 120 BPM = 960 ticks per second.
    private const int TicksPerQuarter = 480;

    public static void Save(string path, Score score)
    {
        score.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var track = new MemoryStream();
        using (var writer = new BinaryWriter(track, Encoding.UTF8, true))
        {
            writer.Write((byte)0); // delta time
            writer.Write(new byte[] { 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 }); // 120 BPM
            var name = Encoding.UTF8.GetBytes(score.Title);
            writer.Write((byte)0);
            writer.Write(new byte[] { 0xFF, 0x03 });
            WriteVariableLength(writer, (uint)name.Length);
            writer.Write(name);
            writer.Write((byte)0);
            writer.Write(new byte[] { 0xC0, 22 }); // General MIDI harmonica

            var events = score.Notes.SelectMany(note => new[]
            {
                (Tick: ToTick(note.StartMs), On: true, Note: note.Midi),
                (Tick: ToTick(note.EndMs), On: false, Note: note.Midi)
            }).OrderBy(e => e.Tick).ThenBy(e => e.On).ToArray();
            var previous = 0;
            foreach (var noteEvent in events)
            {
                WriteVariableLength(writer, (uint)(noteEvent.Tick - previous));
                writer.Write((byte)(noteEvent.On ? 0x90 : 0x80));
                writer.Write((byte)noteEvent.Note);
                writer.Write((byte)(noteEvent.On ? 90 : 0));
                previous = noteEvent.Tick;
            }
            writer.Write((byte)0);
            writer.Write(new byte[] { 0xFF, 0x2F, 0 });
        }

        var temp = path + ".tmp";
        try
        {
            using (var output = File.Create(temp))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("MThd"));
                WriteBigEndian(writer, 6, 4);
                WriteBigEndian(writer, 0, 2);
                WriteBigEndian(writer, 1, 2);
                WriteBigEndian(writer, TicksPerQuarter, 2);
                writer.Write(Encoding.ASCII.GetBytes("MTrk"));
                WriteBigEndian(writer, (int)track.Length, 4);
                track.WriteTo(output);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static int ToTick(int ms) => checked((int)Math.Round(ms * 0.96));

    private static void WriteBigEndian(BinaryWriter writer, int value, int bytes)
    {
        for (var shift = (bytes - 1) * 8; shift >= 0; shift -= 8)
            writer.Write((byte)(value >> shift));
    }

    private static void WriteVariableLength(BinaryWriter writer, uint value)
    {
        var encoded = value & 0x7F;
        while ((value >>= 7) != 0) encoded = (encoded << 8) | ((value & 0x7F) | 0x80);
        while (true)
        {
            writer.Write((byte)encoded);
            if ((encoded & 0x80) == 0) break;
            encoded >>= 8;
        }
    }
}
