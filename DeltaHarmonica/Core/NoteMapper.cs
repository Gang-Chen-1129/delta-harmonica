namespace DeltaHarmonica.Core;

public readonly record struct KeyPlan(char Key, bool Low, bool High, bool Sharp)
{
    public override string ToString() => $"{Key}{(Low ? " + 左键" : "")}{(High ? " + 右键" : "")}{(Sharp ? " + 中键" : "")}";
}

public static class NoteMapper
{
    private static readonly char[] Keys = ['Z', 'X', 'C', 'V', 'B', 'N', 'M', ','];
    private static readonly int[] Semitones = [0, 2, 4, 5, 7, 9, 11, 12];

    public static bool TryMap(int midi, int baseMidi, out KeyPlan plan)
    {
        KeyPlan? best = null;
        var bestCost = int.MaxValue;
        for (var octave = -1; octave <= 1; octave++)
        for (var sharp = 0; sharp <= 1; sharp++)
        for (var i = 0; i < Keys.Length; i++)
        {
            if (baseMidi + octave * 12 + Semitones[i] + sharp != midi) continue;
            var cost = (octave == 0 ? 0 : 2) + sharp + (i == 7 ? 1 : 0);
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = new KeyPlan(Keys[i], octave == -1, octave == 1, sharp == 1);
        }
        plan = best ?? default;
        return best.HasValue;
    }

    public static (int Minimum, int Maximum) Range(int baseMidi) => (baseMidi - 12, baseMidi + 25);

    public static int BestOctaveTranspose(IEnumerable<NoteEvent> notes, int baseMidi)
    {
        var source = notes.ToArray();
        var options = Enumerable.Range(-5, 11).Select(o => o * 12);
        return options.OrderBy(shift => source.Count(n => !TryMap(n.Midi + shift, baseMidi, out _)))
            .ThenBy(Math.Abs).First();
    }
}
