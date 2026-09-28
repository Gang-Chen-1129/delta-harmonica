using System.Diagnostics;

namespace DeltaHarmonica.Core;

public sealed class PlaybackEngine
{
    private const double ModifierLeadMs = 40;
    private const double MinimumKeyHoldMs = 45;
    private const double ReleaseGapMs = 40;
    private readonly WinInput _input = new();
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    public bool IsPlaying => _task is { IsCompleted: false };
    public event Action<int, int>? Progress;
    public event Action<string>? Finished;

    public void Start(Score score, int baseMidi, int transpose, double speed)
    {
        if (IsPlaying) throw new InvalidOperationException("正在播放。请先停止当前曲目。");
        score.Validate();
        if (speed is < 0.25 or > 2.0) throw new ArgumentOutOfRangeException(nameof(speed));
        var sequence = score.Notes.OrderBy(n => n.StartMs).Select(n =>
        {
            if (!NoteMapper.TryMap(n.Midi + transpose, baseMidi, out var plan))
                throw new InvalidDataException($"音高 {n.Midi + transpose} 超出演奏范围。请调整基准音或移调。");
            return (Note: n, Plan: plan);
        }).ToArray();
        _cancellation = new CancellationTokenSource();
        _task = Task.Run(() => PlayAsync(sequence, speed, _cancellation.Token));
    }

    public async Task StopAsync()
    {
        _cancellation?.Cancel();
        if (_task is not null)
            try { await _task; } catch (OperationCanceledException) { }
        _input.ReleaseAll();
    }

    private async Task PlayAsync((NoteEvent Note, KeyPlan Plan)[] sequence, double speed, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var lastReleaseMs = double.NegativeInfinity;
        try
        {
            for (var i = 0; i < sequence.Length; i++)
            {
                var (note, plan) = sequence[i];
                await WaitUntilAsync(watch, Math.Max(note.StartMs / speed, lastReleaseMs + ReleaseGapMs), token);
                token.ThrowIfCancellationRequested();
                _input.PrepareModifiers(plan);
                try
                {
                    if (plan.Low || plan.High || plan.Sharp)
                        await Task.Delay(TimeSpan.FromMilliseconds(ModifierLeadMs), token);
                    token.ThrowIfCancellationRequested();
                    _input.PressKey(plan);
                    var keyDownMs = watch.Elapsed.TotalMilliseconds;
                    Progress?.Invoke(i + 1, sequence.Length);
                    await WaitUntilAsync(watch, keyDownMs + Math.Max(MinimumKeyHoldMs, note.DurationMs / speed), token);
                }
                finally { _input.Release(plan); }
                lastReleaseMs = watch.Elapsed.TotalMilliseconds;
            }
            Finished?.Invoke("播放完成");
        }
        catch (OperationCanceledException) { Finished?.Invoke("已停止"); }
        catch (Exception ex) { Finished?.Invoke("播放失败：" + ex.Message); }
        finally { _input.ReleaseAll(); }
    }

    private static async Task WaitUntilAsync(Stopwatch watch, double targetMs, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var remaining = targetMs - watch.Elapsed.TotalMilliseconds;
            if (remaining <= 0) return;
            if (remaining > 8) await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining - 3, 50)), token);
            else await Task.Yield();
        }
    }
}
