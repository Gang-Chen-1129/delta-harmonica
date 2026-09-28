namespace DeltaHarmonica.Core;

/// <summary>Fits a selected MIDI melody into the playable keyboard range.</summary>
public static class MidiSongConverter
{
    public static Score ToPlayableScore(Score source, int baseMidi, bool trimLeadingSilence)
    {
        source.Validate();
        var firstStart = trimLeadingSilence ? source.Notes.Min(n => n.StartMs) : 0;
        var shift = NoteMapper.BestOctaveTranspose(source.Notes, baseMidi);
        var result = new Score { Title = source.Title };
        foreach (var note in source.Notes.OrderBy(n => n.StartMs))
        {
            var target = note.Midi + shift;
            if (target is < 0 or > 127 || !NoteMapper.TryMap(target, baseMidi, out _))
                target = Enumerable.Range(-10, 21).Select(o => target + 12 * o)
                    .Where(p => p is >= 0 and <= 127 && NoteMapper.TryMap(p, baseMidi, out _))
                    .OrderBy(p => Math.Abs(p - target)).First();
            result.Notes.Add(new NoteEvent(note.StartMs - firstStart, target, note.DurationMs));
        }
        result.Validate();
        return result;
    }
}
