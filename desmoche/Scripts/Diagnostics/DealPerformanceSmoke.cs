using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Mide solo el trabajo de Mesa._Process. No cambia el ritmo ni la autoridad.
public partial class DealPerformanceSmoke : Mesa
{
    private sealed record Sample(double Seconds, double Milliseconds, long Bytes, bool Finished);
    private readonly List<Sample> _samples = new(1024);
    private double _elapsed;
    private int _ownHides;
    private bool _assertSmooth, _auto;

    public override void _Ready()
    {
        base._Ready();
        string[] args = OS.GetCmdlineUserArgs();
        _assertSmooth = args.Contains("--assert-smooth");
        _auto = args.Contains("--auto");
        Callable.From(Run).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (!IsDealing) { base._Process(delta); return; }
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long beforeTime = Stopwatch.GetTimestamp();
        base._Process(delta);
        double milliseconds = Stopwatch.GetElapsedTime(beforeTime).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        _elapsed += delta;
        _samples.Add(new Sample(_elapsed, milliseconds, bytes, !IsDealing));
    }

    private async void Run()
    {
        try
        {
            GetWindow().Size = new Vector2I(1600, 900);
            long window = 2;
            foreach (var scenario in new[] { (2, true), (3, true), (4, true), (4, false) })
            {
                int count = scenario.Item1;
                bool queued = scenario.Item2;
                DisplaySnapshot(null, "Preparando medicion");
                _samples.Clear(); _elapsed = 0; _ownHides = 0;
                SnapshotDto view = Fixture(count) with { Window = window++ };
                DisplaySnapshot(view, "Medicion del reparto");
                ulong[] before = Hand().Select(c => c.GetInstanceId()).ToArray();
                await Until(() => AnimatedDealCardCount == count * 9);
                foreach (Carta card in Flights())
                    card.VisibilityChanged += () => { if (card.IsFaceUp && !card.Visible) _ownHides++; };
                await ToSignal(GetTree().CreateTimer(0.9), Timer.SignalName.Timeout);
                if (queued)
                    DisplaySnapshot(view with { Revision = 3, ExchangeCount = 1,
                        Players = view.Players.Select(p => p.Id == 1 ? p with { CardCount = 8 } : p).ToArray() },
                        "Confirmacion ajena durante el reparto");
                await Until(() => !IsDealing);
                bool sameHand = before.SequenceEqual(Hand().Select(c => c.GetInstanceId()));
                int repeatedHides = Math.Max(0, _ownHides - 9);
                Sample finish = _samples.Last(s => s.Finished);
                Sample[] late = _samples.Where(s => !s.Finished && s.Seconds >= finish.Seconds - 0.65).ToArray();
                double[] times = late.Select(s => s.Milliseconds).OrderBy(t => t).ToArray();
                GD.Print("DEAL PERF " + JsonSerializer.Serialize(new {
                    Players = count, QueuedUpdate = queued, Frames = _samples.Count,
                    LateMeanMilliseconds = Math.Round(late.Average(s => s.Milliseconds), 4),
                    LateP95Milliseconds = Math.Round(times[(int)Math.Floor((times.Length - 1) * 0.95)], 4),
                    LateMeanAllocatedBytes = Math.Round(late.Average(s => s.Bytes), 1),
                    FinishMilliseconds = Math.Round(finish.Milliseconds, 4), FinishAllocatedBytes = finish.Bytes,
                    RepeatedHides = repeatedHides, PreservedHand = sameHand
                }));
                if (_assertSmooth && (repeatedHides != 0 || !sameHand))
                    throw new InvalidOperationException("El cierre vuelve a activar cartas terminadas o reconstruye la mano.");
            }
            GD.Print("DEAL PERFORMANCE PASS: cierre y entregas terminadas verificados.");
            if (_auto) GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("DEAL PERFORMANCE FAIL: " + error.Message); if (_auto) GetTree().Quit(1); }
    }

    private Carta[] Hand() => GetNode<Control>(ContentPath + "HandZone/Padding/Content/Cards")
        .GetChildren().OfType<Carta>().ToArray();
    private Carta[] Flights() => GetNode<Control>(ContentPath + "DealAnimation")
        .GetChildren().OfType<Carta>().ToArray();
    private async Task Until(Func<bool> predicate)
    {
        ulong end = Time.GetTicksMsec() + 7000;
        while (!predicate())
        { if (Time.GetTicksMsec() >= end) throw new InvalidOperationException("Tiempo de prueba agotado."); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
    private static SnapshotDto Fixture(int count) => new(NetworkProtocol.Version,
        new string((char)('a' + count), 32), 2, 2, count, GamePhase.InitialExchange, MatchResult.None, null, null, null,
        Enumerable.Range(1, count).Select(id => new PublicPlayerDto(id, "Jugador " + id, id - 1, 9, true, false)).ToArray(),
        Enumerable.Range((count - 1) * 9, 9).ToArray(), Array.Empty<MeldDto>(),
        52 - count * 9, 0, null, null, Array.Empty<RevealedHandDto>(), false);
}
