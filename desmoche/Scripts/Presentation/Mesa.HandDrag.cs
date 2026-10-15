using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    public const string HandDragSlotPath = ContentPath + "HandDragSlot";
    private Panel _handDragSlot = null!;
    private Carta? _draggedHandCard;
    private Carta[] _handDragCards = Array.Empty<Carta>();
    private Vector2[] _handDragSlots = Array.Empty<Vector2>();
    private Transform2D _handDragTransform;
    private Color _handDragColor;
    private int _handDragTarget;
    private bool _handDragCommitted, _handDragInside;

    private void InitializeHandDrag()
    {
        _handDragSlot = new Panel { Name = "HandDragSlot", Visible = false,
            MouseFilter = MouseFilterEnum.Ignore, ZIndex = 4 };
        _handDragSlot.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.75f, 0.55f, 0.15f, 0.12f),
            BorderColor = new Color(0.92f, 0.73f, 0.30f, 0.95f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ExpandMarginLeft = 4, ExpandMarginRight = 4, ExpandMarginTop = 4, ExpandMarginBottom = 4,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
            ShadowColor = new Color(0, 0, 0, 0.16f), ShadowSize = 4
        });
        _casinoCanvas.AddChild(_handDragSlot);
        Label label = Text("Aquí", 22, new Color(0.98f, 0.81f, 0.43f));
        label.Name = "DestinationLabel";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        _handDragSlot.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private void BeginHandDragVisual(Carta source)
    {
        AbortHandDrag();
        _handDragCards = VisibleHandCards();
        _handDragSlots = _handDragCards.Select(card => card.Position).ToArray();
        _handDragTransform = _hand.GetGlobalTransformWithCanvas();
        _draggedHandCard = source;
        _handDragColor = source.Modulate;
        source.Modulate = new Color(_handDragColor.R, _handDragColor.G, _handDragColor.B, 0);
        _handDragTarget = Array.IndexOf(_handDragCards, source);
        _handDragCommitted = false;
        UpdateHandDragPreview(_hand.GetLocalMousePosition());
    }

    // El destino se calcula desde los espacios estables, no desde las cartas
    // que están moviéndose. Evita que el hueco oscile entre dos vecinas.
    private static int NearestHandSlot(Vector2 point, Vector2[] slots, Vector2 cardSize)
    {
        int nearest = 0; float best = float.MaxValue;
        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 distance = point - (slots[i] + cardSize / 2);
            float score = distance.LengthSquared();
            if (score < best) { best = score; nearest = i; }
        }
        return nearest;
    }

    private int HandDropIndex(Vector2 point)
    {
        Carta[] cards = VisibleHandCards();
        if (cards.Length == 0) return 0;
        int nearest = NearestHandSlot(point, cards.Select(card => card.Position).ToArray(), cards[0].Size);
        return HandModelBeforeIndex(cards,
            nearest + (point.X >= cards[nearest].Position.X + cards[nearest].Size.X / 2 ? 1 : 0));
    }

    private Carta[] VisibleHandCards() => _hand.GetChildren().OfType<Carta>().Where(card => card.Visible).ToArray();

    // La carta preparada sigue en el orden local hasta confirmación, aunque
    // ya no ocupe un espacio visible. Traducir el hueco al índice del modelo.
    private int HandModelBeforeIndex(Carta[] cards, int beforeIndex) =>
        beforeIndex >= cards.Length ? _handState.Order.Count :
        _handState.Order.ToList().IndexOf(cards[beforeIndex].FaceCardId!.Value);

    private void UpdateHandDragPreview(Vector2 point)
    {
        if (_draggedHandCard is not { } source || _handDragCommitted || _handDragCards.Length == 0) return;
        _handDragInside = new Rect2(new Vector2(0, -source.Size.Y * 0.35f),
            _hand.Size + new Vector2(0, source.Size.Y * 0.35f)).HasPoint(point);
        _handDragSlot.Visible = _handDragInside;
        if (!_handDragInside)
        {
            foreach (Carta card in _handDragCards) card.SetReorderOffset(Vector2.Zero);
            return;
        }
        _handDragTarget = NearestHandSlot(point, _handDragSlots, source.Size);
        var previewOrder = _handDragCards.Where(card => card != source).ToList();
        previewOrder.Insert(_handDragTarget, source);
        for (int i = 0; i < previewOrder.Count; i++)
            if (previewOrder[i] != source)
                previewOrder[i].SetReorderOffset(_handDragSlots[i] - previewOrder[i].Position);
        Vector2 global = _hand.GetGlobalTransformWithCanvas() * _handDragSlots[_handDragTarget];
        _handDragSlot.Position = _casinoCanvas.GetGlobalTransformWithCanvas().AffineInverse() * global;
        _handDragSlot.Size = source.Size;
    }

    private void ProcessHandDrag()
    {
        if (_draggedHandCard is null) return;
        if (!CanEdit || !IsVisibleInTree() || !IsInstanceValid(_draggedHandCard) ||
            !_hand.GetGlobalTransformWithCanvas().IsEqualApprox(_handDragTransform))
        { AbortHandDrag(); if (GetViewport().GuiIsDragging()) GetViewport().GuiCancelDrag(); return; }
        if (!GetViewport().GuiIsDragging()) { CompleteHandDragVisual(false); return; }
        UpdateHandDragPreview(_hand.GetLocalMousePosition());
    }

    private bool ProcessHandDragInput(InputEvent input)
    {
        if (_draggedHandCard is null) return false;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (GetViewport().GuiIsDragging()) GetViewport().GuiCancelDrag();
            CompleteHandDragVisual(false); return true;
        }
        // Actualizar antes del release nativo: incluso un movimiento y soltado
        // en el mismo cuadro usan el hueco que ve el jugador en ese punto.
        if (input is InputEventMouse mouse)
            UpdateHandDragPreview(_hand.GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position);
        return false;
    }

    private void AnimateHandOrderChange(int? draggedId = null)
    {
        Carta[] oldOrder = VisibleHandCards();
        Vector2[] positions = oldOrder.Select(card => card.Position).ToArray();
        var cards = _hand.GetChildren().OfType<Carta>().ToDictionary(card => card.FaceCardId!.Value);
        var visual = oldOrder.ToDictionary(card => card.FaceCardId!.Value,
            card => card.Position + card.OffsetTransformPosition);
        if (draggedId is int movedId && _draggedHandCard?.FaceCardId == movedId)
        {
            Carta source = cards[movedId];
            visual[movedId] = _hand.GetLocalMousePosition() - source.Size / 2 - source.OffsetTransformPositionRatio * source.Size;
            _handDragCommitted = true;
        }
        int index = 0, visibleIndex = 0;
        foreach (int id in _handState.Order)
        {
            Carta card = cards[id];
            _hand.MoveChild(card, index++);
            if (!card.Visible) continue;
            // La colocación que realizará HFlow es la misma; aplicar primero
            // el desplazamiento evita un cuadro de salto al confirmar.
            card.Position = positions[visibleIndex];
            card.SetReorderOffset(visual[id] - positions[visibleIndex], false);
            if (_draggedHandCard is null) card.SetReorderOffset(Vector2.Zero);
            visibleIndex++;
        }
        _hand.QueueSort();
        RefreshSelection();
    }

    private void CompleteHandDragVisual(bool _) => EndHandDragVisual(true);
    private void AbortHandDrag() => EndHandDragVisual(false);

    private void EndHandDragVisual(bool animate)
    {
        Carta? source = _draggedHandCard;
        if (source is null) return;
        if (IsInstanceValid(source))
        {
            if (animate && source.Visible && !_handDragCommitted)
                source.SetReorderOffset(_hand.GetLocalMousePosition() - source.Size / 2 -
                    source.Position - source.OffsetTransformPositionRatio * source.Size, false);
            source.Modulate = _handDragColor;
        }
        _draggedHandCard = null;
        if (_handDragSlot is not null) _handDragSlot.Hide();
        foreach (Carta card in _handDragCards)
            if (IsInstanceValid(card) && card.IsInsideTree()) card.SetReorderOffset(Vector2.Zero, animate && card.Visible);
        _handDragCards = Array.Empty<Carta>(); _handDragSlots = Array.Empty<Vector2>();
        _handDragCommitted = _handDragInside = false;
    }
}
