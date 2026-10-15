using System.Collections.Generic;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    public override void _Input(InputEvent input)
    {
        if (_ready && ProcessHandDragInput(input))
        { GetViewport().SetInputAsHandled(); return; }
        if (_ready && CloseTablePopupOnInput(input))
        { GetViewport().SetInputAsHandled(); return; }
        if (_ready && IsVisibleInTree() && CloseFinalHandOnInput(input))
        { GetViewport().SetInputAsHandled(); return; }
        if (!_ready || !IsVisibleInTree() || !CanEdit ||
            input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse ||
            GetViewport().GuiIsDragging() ||
            _handState.SelectedIds.Count == 0 && !_handState.ExternalSelected) return;

        if (!GetViewportRect().HasPoint(mouse.Position)) return;
        // Los Control vacíos también pueden consumir el evento. Se comprueba
        // la geometría antes de la GUI, sin aceptar el evento ni tocar botones.
        foreach (Control control in SelectionControls(this))
        {
            if (!control.IsVisibleInTree()) continue;
            if (control is Carta || control is BaseButton or ScrollBar or LineEdit or TextEdit)
                if (ContainsPointer(control, mouse.Position)) return;
        }
        _handState.ClearSelection();
        RefreshSelection();
    }

    private static bool ContainsPointer(Control control, Vector2 viewportPoint) =>
        new Rect2(Vector2.Zero, control.Size).HasPoint(
            control.GetGlobalTransformWithCanvas().AffineInverse() * viewportPoint);

    private static IEnumerable<Control> SelectionControls(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Control control) yield return control;
            foreach (Control nested in SelectionControls(child)) yield return nested;
        }
    }
}
