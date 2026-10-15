using System;
using Desmoche.Domain;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    public const string ChoiceClockPath = ContentPath + "ChoiceClock";
    public const string PreparationClockPath = ContentPath + "PreparationClock";
    public const string PasaClockPath = ContentPath + "PasaClock";
    private MesaActionClock _choiceClock = null!, _preparationClock = null!, _pasaClock = null!;
    private bool _tableActionsReady, _pasaPositioned;
    private Rect2 _lastPasaAnchor;

    private void InitializeTableActions()
    {
        MesaActionClock Clock(string name)
        {
            var clock = new MesaActionClock { Name = name, Visible = false, ZIndex = 14 };
            _casinoCanvas.AddChild(clock); return clock;
        }
        _choiceClock = Clock("ChoiceClock"); _preparationClock = Clock("PreparationClock"); _pasaClock = Clock("PasaClock");
        _contextActions.MouseFilter = MouseFilterEnum.Ignore;
        _contextActions.ZIndex = 8;
        _pasaButton.CustomMinimumSize = new Vector2(88, 44);
        foreach (Button button in _contextActions.GetChildren().OfType<Button>())
            button.AddThemeFontSizeOverride("font_size", 14);
        foreach (var entry in new[] { (ActionKind.ChooseOffer, "Tomar carta"), (ActionKind.ChooseDiscard, "Tomar pozo"),
            (ActionKind.Pass, "Rechazar"), (ActionKind.DrawDeck, "Del mazo"), (ActionKind.Publish, "Confirmar juegos"),
            (ActionKind.DrawDiscard, "Confirmar juego"), (ActionKind.FinishInitial, "Continuar"),
            (ActionKind.CompleteWithoutPayment, "Finalizar pago") })
        {
            Button button = _contextButtons[entry.Item1];
            if (button.TooltipText.Length == 0) button.TooltipText = button.Text;
            button.Text = entry.Item2;
        }
        _cancelCard.Text = "Cambiar carta";
        _cardCountdown.Hide();
        _tableActionsReady = true;
        GetNode<Control>(ContentPath + "TopBar").MoveToFront();
    }

    private void UpdateTableClocks(SnapshotDto? view, int elapsed)
    {
        if (!_tableActionsReady) return;
        int Remaining(int host) => Math.Max(0, host - elapsed);
        bool changed = false;
        void Show(MesaActionClock clock, bool visible, int remaining, int duration, string caption)
        {
            if (clock.Visible != visible) changed = true;
            clock.Visible = visible;
            if (visible) clock.SetRemaining(remaining, duration, caption);
        }
        var own = view?.OfferTiming?.Reservations.FirstOrDefault(r => r.PlayerId == view.RecipientId);
        bool offer = view?.Phase == GamePhase.AwaitingClaims && view.OfferTiming is not null &&
            !view.OwnResponseSubmitted && own is null && _contextButtons[ActionKind.ChooseOffer].Visible;
        bool discard = view?.Phase == GamePhase.AwaitingDraw && view.DiscardTiming?.PlayerId == view.RecipientId &&
            _contextButtons[ActionKind.ChooseDiscard].Visible;
        Show(_choiceClock, offer || discard, Remaining(offer ? view!.OfferTiming!.RemainingSelectionMilliseconds :
            discard ? view!.DiscardTiming!.RemainingMilliseconds : 0), (view?.Settings ?? MatchSettings.Default).CardChoiceSeconds * 1000, discard ? "Elegir pozo" : "Elegir carta");
        Show(_preparationClock, view?.Phase == GamePhase.AwaitingClaims && own is not null,
            Remaining(own?.RemainingMilliseconds ?? 0), (view?.Settings ?? MatchSettings.Default).CardPreparationSeconds * 1000, "Preparar juego");
        Show(_pasaClock, view?.Phase == GamePhase.AwaitingOrderChallenge && view.OrderChallenge is not null,
            Remaining(view?.OrderChallenge?.RemainingMilliseconds ?? 0), (view?.Settings ?? MatchSettings.Default).PasaSeconds * 1000, "Pasa");
        _cardCountdown.Hide();
        if (changed) { _pasaPositioned = false; QueueCasinoLayout(); }
    }

    private Rect2 ScreenRect(Control control)
    {
        Transform2D inverse = GetGlobalTransformWithCanvas().AffineInverse();
        Transform2D transform = control.GetGlobalTransformWithCanvas();
        Vector2 start = inverse * (transform * Vector2.Zero), end = inverse * (transform * control.Size);
        return new Rect2(start, end - start);
    }

    private List<Rect2> TableActionObstacles()
    {
        var obstacles = new List<Rect2>();
        foreach (Control control in SelectionControls(_casinoCanvas))
        {
            if (!control.IsVisibleInTree()) continue;
            bool card = control is Carta;
            bool ownButton = control is Button && (_ownGames.IsAncestorOf(control) || _draftRows.IsAncestorOf(control));
            if (!card && !ownButton) continue;
            Rect2 rect = ScreenRect(control);
            for (Node? parent = control.GetParent(); parent is not null && parent != _casinoCanvas; parent = parent.GetParent())
                if (parent is Control { ClipContents: true } clip) rect = rect.Intersection(ScreenRect(clip));
            if (rect.Size.X > 0 && rect.Size.Y > 0) obstacles.Add(rect.Grow(5));
        }
        foreach (Control player in _players.GetChildren().OfType<Control>()) obstacles.Add(ScreenRect(player).Grow(4));
        // Reservar la elevación seleccionada para que los botones no cambien
        // de sitio durante la animación de la mano.
        Rect2 hand = ScreenRect(_hand);
        obstacles.Add(new Rect2(hand.Position - new Vector2(0, 128 * CasinoScale * .18f + 8),
            hand.Size + new Vector2(0, 128 * CasinoScale * .18f + 8)));
        foreach (Label label in SelectionControls(_casinoCanvas).OfType<Label>())
        {
            if (!label.IsVisibleInTree() || _contextActions.IsAncestorOf(label) ||
                _tableStatus.IsAncestorOf(label) || _tableInfo.IsAncestorOf(label) ||
                label.GetParent() is MesaActionClock || label.Text.Length == 0) continue;
            bool insideCard = false;
            for (Node? parent = label.GetParent(); parent is not null && parent != _casinoCanvas; parent = parent.GetParent())
                if (parent is Carta) { insideCard = true; break; }
            if (insideCard) continue; // La carta completa ya está reservada.
            Rect2 rect = ScreenRect(label);
            float textWidth = label.GetThemeFont("font").GetStringSize(label.Text,
                fontSize: label.GetThemeFontSize("font_size")).X * label.GetGlobalTransformWithCanvas().Scale.X /
                GetGlobalTransformWithCanvas().Scale.X;
            if (label.HorizontalAlignment == HorizontalAlignment.Center && textWidth < rect.Size.X)
                rect = new Rect2(new Vector2(rect.GetCenter().X - textWidth / 2, rect.Position.Y), new Vector2(textWidth, rect.Size.Y));
            obstacles.Add(rect.Grow(3));
        }
        if (_tablePrevious.Visible) { obstacles.Add(ScreenRect(_tablePrevious).Grow(4)); obstacles.Add(ScreenRect(_tableNext).Grow(4)); }
        return obstacles;
    }

    private sealed record ActionPlacement(Control Control, Rect2 Rect);

    private List<ActionPlacement> PackTableActions(Button[] buttons, MesaActionClock? clock, int columns)
    {
        var result = new List<ActionPlacement>(); float x = 0, y = 0;
        for (int index = 0; index < buttons.Length; index++)
        {
            if (index > 0 && index % columns == 0) { x = 0; y += 52; }
            float width = Math.Max(64, buttons[index].GetCombinedMinimumSize().X);
            result.Add(new(buttons[index], new Rect2(x, y, width, 44))); x += width + 8;
            if (index == 0 && clock?.Visible == true)
            { result.Add(new(clock, new Rect2(x, y, 44, 44))); x += 52; }
        }
        if (buttons.Length == 0 && clock?.Visible == true) result.Add(new(clock, new Rect2(0, 0, 44, 44)));
        return result;
    }

    private void PlaceTableActionGroup(Button[] buttons, MesaActionClock? clock, Rect2 anchor,
        List<Rect2> obstacles, bool vertical = false)
    {
        buttons = buttons.Where(b => b.Visible).ToArray();
        if (buttons.Length == 0 && clock?.Visible != true) return;
        Rect2 board = ScreenRect(GetNode<Control>(ContentPath + "Material")).Grow(-10);
        List<ActionPlacement>? chosen = null; Vector2 position = Vector2.Zero;
        float best = float.MaxValue;
        foreach (int columns in (vertical ? new[] { 1, Math.Max(1, buttons.Length) } : new[] { Math.Max(1, buttons.Length), 1 }).Distinct())
        {
            List<ActionPlacement> packed = PackTableActions(buttons, clock, columns);
            Vector2 size = new(packed.Max(p => p.Rect.End.X), packed.Max(p => p.Rect.End.Y));
            bool Fits(Vector2 at) => board.Encloses(new Rect2(at, size)) &&
                packed.All(p => !obstacles.Any(o => o.Intersects(new Rect2(at + p.Rect.Position, p.Rect.Size).Grow(2))));
            var candidates = new List<Vector2>
            {
                new(anchor.End.X + 10, anchor.Position.Y),
                new(anchor.Position.X - size.X - 10, anchor.Position.Y),
                new(anchor.GetCenter().X - size.X / 2, anchor.End.Y + 10),
                new(anchor.GetCenter().X - size.X / 2, anchor.Position.Y - size.Y - 10)
            };
            Vector2? found = candidates.Where(Fits).Select(p => (Vector2?)p).FirstOrDefault();
            if (found is not null) { chosen = packed; position = found.Value; break; }
            for (float y = board.Position.Y; y + size.Y <= board.End.Y; y += 12)
            for (float x = board.Position.X; x + size.X <= board.End.X; x += 12)
            {
                Vector2 at = new(x, y); if (!Fits(at)) continue;
                float distance = (at + size / 2).DistanceSquaredTo(anchor.GetCenter());
                if (distance >= best) continue;
                best = distance; chosen = packed; position = at;
            }
        }
        if (chosen is null)
        {
            chosen = PackTableActions(buttons, clock, 1);
            Vector2 size = new(chosen.Max(p => p.Rect.End.X), chosen.Max(p => p.Rect.End.Y));
            position = new Vector2(Math.Clamp(anchor.End.X + 10, board.Position.X, Math.Max(board.Position.X, board.End.X - size.X)),
                Math.Clamp(anchor.Position.Y, board.Position.Y, Math.Max(board.Position.Y, board.End.Y - size.Y)));
        }
        foreach (ActionPlacement item in chosen)
        {
            Vector2 at = position + item.Rect.Position;
            if (item.Control is MesaActionClock || item.Control == _pasaButton) PlaceScreenControl(item.Control, at, item.Rect.Size);
            else { item.Control.Scale = Vector2.One; item.Control.Position = at; item.Control.Size = item.Rect.Size; }
            obstacles.Add(new Rect2(at, item.Rect.Size).Grow(5));
        }
    }

    private void LayoutInTableActions()
    {
        if (!_tableActionsReady) return;
        PlaceScreenControl(_contextActions, Vector2.Zero, Size);
        var obstacles = TableActionObstacles();
        Button B(ActionKind kind) => _contextButtons[kind];
        Carta source = _offered.IsVisibleInTree() ? _offered : _discard;
        PlaceTableActionGroup(new[] { B(ActionKind.ChooseOffer), B(ActionKind.Pass) },
            B(ActionKind.ChooseOffer).Visible ? _choiceClock : null, ScreenRect((Control)source.GetParent()), obstacles, true);
        PlaceTableActionGroup(new[] { B(ActionKind.ChooseDiscard) },
            _interactionView?.DiscardTiming is not null ? _choiceClock : null, ScreenRect((Control)_discard.GetParent()), obstacles);
        PlaceTableActionGroup(new[] { B(ActionKind.DrawDeck) }, null, ScreenRect((Control)_deck.GetParent()), obstacles);
        PlaceTableActionGroup(new[] { _confirmCard, _cancelCard, B(ActionKind.CompleteWithoutPayment) },
            null, ScreenRect((Control)_stagedCard.GetParent()), obstacles, true);
        var games = new[] { B(ActionKind.Claim), B(ActionKind.DrawDiscard), B(ActionKind.Publish), _tablePrepare,
            _declareInitial, _declareColor, _declarePeladilla, B(ActionKind.FinishInitial) };
        PlaceTableActionGroup(games, _preparationClock, ScreenRect(_ownGames), obstacles);
        _pasaPositioned = false; LayoutPasaAction();
    }

    private void LayoutPasaAction()
    {
        if (!_tableActionsReady || (!_pasaButton.Visible && !_pasaClock.Visible) ||
            _challengedMeld is null || !IsInstanceValid(_challengedMeld)) return;
        Rect2[] cards = _challengedMeld.GetChildren().OfType<Carta>().Select(ScreenRect).ToArray();
        Rect2 anchor = cards.Length == 0 ? ScreenRect(_challengedMeld) : new Rect2(
            new Vector2(cards.Min(r => r.Position.X), cards.Min(r => r.Position.Y)),
            new Vector2(cards.Max(r => r.End.X) - cards.Min(r => r.Position.X), cards.Max(r => r.End.Y) - cards.Min(r => r.Position.Y)));
        if (_pasaPositioned && anchor == _lastPasaAnchor) return;
        _lastPasaAnchor = anchor; _pasaPositioned = true;
        var obstacles = TableActionObstacles();
        obstacles.AddRange(_contextActions.GetChildren().OfType<Control>().Where(c => c.Visible).Select(c => ScreenRect(c).Grow(5)));
        PlaceTableActionGroup(new[] { _pasaButton }, _pasaClock, anchor, obstacles);
    }
}
