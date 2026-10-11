using System;
using Godot;

namespace Desmoche.Presentation;

// Destino visual genérico. Mesa interpreta el payload y genera la solicitud correspondiente.
public partial class CardDropZone : PanelContainer
{
    public bool DropEnabled { get; set; } = true;
    public Func<Vector2, Variant, bool>? DropAllowed { get; set; }
    public Action<Vector2, Variant>? DropReceived { get; set; }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        DropEnabled && DropAllowed?.Invoke(atPosition, data) == true;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        // La vista puede haber cambiado desde la consulta de Godot.
        if (_CanDropData(atPosition, data)) DropReceived?.Invoke(atPosition, data);
    }

    public override void _ExitTree()
    {
        DropAllowed = null;
        DropReceived = null;
    }
}
