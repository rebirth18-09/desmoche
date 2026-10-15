using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation.Interaction;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    private sealed class DealFlight
    {
        public DealVisualCard Plan { get; }
        public Carta Visual { get; }
        public Carta? Own { get; }
        public Control? RivalRows { get; }
        public bool Started { get; set; }
        public bool Completed { get; set; }
        public bool Revealed { get; set; }
        public DealFlight(DealVisualCard plan, Carta visual, Carta? own, Control? rivalRows)
        { Plan = plan; Visual = visual; Own = own; RivalRows = rivalRows; }
    }
    private Control _dealLayer = null!;
    private readonly List<DealFlight> _dealFlights = new();
    private readonly Dictionary<int, Label> _dealPlayerCounts = new();
    private SnapshotDto? _dealLatest;
    private string _dealStatus = "", _dealSession = "";
    private long _dealWindow;
    private double _dealElapsed;
    private int _dealLayoutFrames;
    public bool IsDealing { get; private set; }
    public int AnimatedDealCardCount => _dealFlights.Count;

    private void InitializeDealAnimation()
    {
        _dealLayer = new Control { Name = "DealAnimation", MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 20, Visible = false, Size = new Vector2(CanvasWidth, CanvasHeight) };
        _casinoCanvas.AddChild(_dealLayer);
    }

    private bool ShouldAnimateDeal(SnapshotDto? previous, SnapshotDto? view)
    {
        if (view is null || _dealSession == view.Session && _dealWindow == view.Window) return false;
        return InitialDealPresentation.CanStart(previous, view);
    }

    private bool QueueDealSnapshot(SnapshotDto? view, string status)
    {
        if (!IsDealing) return false;
        bool same = view is not null && _dealLatest is { } old &&
            view.Session == _dealSession && view.Window == _dealWindow &&
            InitialDealPresentation.CanStart(null, view) && view.RecipientId == old.RecipientId &&
            view.OwnSpectating == old.OwnSpectating && view.OwnHand.SequenceEqual(old.OwnHand) &&
            view.DeckCount == old.DeckCount && view.DiscardTop == old.DiscardTop &&
            view.DiscardCount == old.DiscardCount && view.OfferedCard == old.OfferedCard &&
            SameDealPlayers(view.Players, old.Players);
        if (!same) { CancelDealAnimation(); return false; }
        // Se conserva la ultima vista confirmada; solo se difiere su dibujo.
        // Si cambia nuestra mano, fase o sesion, se cancela inmediatamente.
        _dealLatest = view; _dealStatus = status;
        return true;
    }

    private static bool SameDealPlayers(PublicPlayerDto[] next, PublicPlayerDto[] previous)
    {
        if (next.Length != previous.Length) return false;
        for (int i = 0; i < next.Length; i++)
            if ((next[i] with { CardCount = previous[i].CardCount }) != previous[i]) return false;
        return true;
    }

    private void BeginDealAnimation(SnapshotDto view, string status)
    {
        _dealSession = view.Session; _dealWindow = view.Window;
        _dealLatest = view; _dealStatus = status;
        _dealElapsed = 0; _dealLayoutFrames = 2;
        IsDealing = true; _dealLayer.Visible = true;
        RefreshSelection();
        foreach (Carta card in _hand.GetChildren().OfType<Carta>()) card.Modulate = new Color(1, 1, 1, 0);
        SetDealEmptyLabels(false);
        _response.Visible = true; _response.Text = "Repartiendo cartas…";
    }

    private void CreateDealFlights()
    {
        // Referencias estables durante el reparto: una vista que las invalida
        // cancela primero la animacion. No buscar nodos en cada cuadro.
        var hand = _hand.GetChildren().OfType<Carta>().ToDictionary(c => c.FaceCardId!.Value);
        foreach (DealVisualCard plan in InitialDealPresentation.Build(_dealLatest!))
        {
            Carta? own = plan.OwnCardId is int id ? hand[id] : null;
            Control? rows = own is null
                ? _rivalGames.GetNodeOrNull<Control>("JuegosJugador" + plan.PlayerId + "/Rows") : null;
            if (own is null && rows is null) { CancelDealAnimation(); RefreshNetworkView(); return; }
            Carta visual = _cardScene.Instantiate<Carta>();
            visual.Name = "Entrega" + _dealFlights.Count;
            visual.SetVisualSize(own?.Size ?? new Vector2(50.3f, 72));
            visual.ShowBack(); visual.Visible = false;
            _dealLayer.AddChild(visual);
            _dealFlights.Add(new DealFlight(plan, visual, own, rows));
        }
    }

    private void ProcessDealAnimation(double delta)
    {
        if (!IsDealing || _dealLatest is null) return;
        if (_dealLayoutFrames > 0) { _dealLayoutFrames--; return; }
        if (_dealFlights.Count == 0) CreateDealFlights();
        if (!IsDealing || _dealLatest is null) return;
        _dealElapsed += Math.Max(0, delta);
        Transform2D inverse = _casinoCanvas.GetGlobalTransform().AffineInverse();
        Rect2 source = _deck.GetGlobalRect();
        Vector2 sourceCenter = inverse * source.GetCenter();
        for (int i = 0; i < _dealFlights.Count; i++)
        {
            DealFlight flight = _dealFlights[i];
            // Las cartas que llegaron ya no necesitan calculos ni cambios de
            // visibilidad. El lienzo compartido mantiene su escala al redimensionar.
            if (flight.Completed) continue;
            double time = _dealElapsed - i * InitialDealPresentation.CardIntervalSeconds;
            if (time < 0) continue;
            Carta? own = flight.Own;
            Vector2 targetCenter, size;
            if (own is not null)
            { size = own.Size; targetCenter = inverse * own.GetGlobalRect().GetCenter(); }
            else
            {
                Control rows = flight.RivalRows!;
                size = new Vector2(50.3f, 72);
                float width = 9 * size.X + 8 * 5;
                Vector2 local = new((RivalWidth - width) / 2 + flight.Plan.Index * (size.X + 5) + size.X / 2, size.Y / 2);
                targetCenter = inverse * (rows.GetGlobalTransform() * local);
            }
            if (flight.Visual.Size != size) flight.Visual.SetVisualSize(size);
            flight.Visual.PivotOffset = size * 0.5f;
            float t = Mathf.Clamp((float)(time / InitialDealPresentation.FlightSeconds), 0, 1);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            Vector2 center = sourceCenter.Lerp(targetCenter, eased);
            center.Y -= Mathf.Sin(t * Mathf.Pi) * 14;
            Vector2 scale = (_deck.Size / size).Lerp(Vector2.One, eased);
            flight.Visual.Rotation = Mathf.Sin(t * Mathf.Pi) * (i % 2 == 0 ? 0.035f : -0.035f);
            flight.Visual.Position = center - size * 0.5f;
            if (!flight.Started) { flight.Visual.Visible = true; flight.Started = true; }
            if (own is not null && time >= InitialDealPresentation.FlightSeconds)
            {
                float flip = Mathf.Clamp((float)((time - InitialDealPresentation.FlightSeconds) /
                    InitialDealPresentation.RevealSeconds), 0, 1);
                scale.X = Math.Max(0.04f, Math.Abs(2 * flip - 1));
                if (flip >= 0.5f && !flight.Revealed)
                { flight.Visual.ShowFace(flight.Plan.OwnCardId!.Value); flight.Revealed = true; }
                if (flip >= 1)
                { flight.Visual.Visible = false; own.Modulate = Colors.White; flight.Completed = true; }
            }
            else if (own is null && t >= 1) flight.Completed = true;
            flight.Visual.Scale = scale;
        }
        if (_dealElapsed < InitialDealPresentation.Duration(_dealFlights.Count)) return;
        SnapshotDto latest = _dealLatest; string status = _dealStatus;
        CancelDealAnimation();
        // La mano y los juegos siguen siendo los mismos. Actualizar la revision
        // y los contadores pendientes conserva nodos, fuentes y posiciones.
        SynchronizeInteraction(latest);
        SynchronizeCountdowns(latest);
        _connection.Text = status;
        foreach (PublicPlayerDto player in latest.Players)
            _dealPlayerCounts[player.Id].Text = player.CardCount + " cartas en mano";
        _response.Visible = true;
        _response.Text = latest.OwnSpectating ? "Observas esta partida." : "Tu respuesta está pendiente.";
        RefreshSelection();
    }

    private void SetDealEmptyLabels(bool visible)
    {
        foreach (VBoxContainer rival in _rivalGames.GetChildren().OfType<VBoxContainer>())
            foreach (Label label in rival.GetNode<Control>("Rows").GetChildren().OfType<Label>())
                label.Modulate = visible ? Colors.White : new Color(1, 1, 1, 0);
    }

    private void CancelDealAnimation()
    {
        IsDealing = false; _dealLatest = null;
        _dealFlights.Clear();
        if (_dealLayer is not null && IsInstanceValid(_dealLayer)) { Clear(_dealLayer); _dealLayer.Visible = false; }
        if (_ready)
        {
            foreach (Carta card in _hand.GetChildren().OfType<Carta>()) card.Modulate = Colors.White;
            SetDealEmptyLabels(true);
        }
    }

    private void DisposeDealAnimation()
    {
        CancelDealAnimation();
        _dealPlayerCounts.Clear();
        _dealSession = ""; _dealWindow = 0;
    }
}
