using Godot;

namespace Desmoche.Presentation;

/// <summary>Ornamento vectorial que conserva sus márgenes al redimensionar la ventana.</summary>
public partial class SalonFrame : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _ExitTree() => Resized -= QueueRedraw;

    public override void _Draw()
    {
        if (Size.X < 80 || Size.Y < 80) return;
        var gold = new Color(0.88f, 0.70f, 0.34f, 0.92f);
        DrawRect(new Rect2(new Vector2(15, 15), Size - new Vector2(30, 30)), gold, false, 1.5f);
        DrawRect(new Rect2(new Vector2(22, 22), Size - new Vector2(44, 44)),
            new Color(0.67f, 0.49f, 0.23f, 0.75f), false);
        foreach (Vector2 corner in new[] { new Vector2(22, 22), new Vector2(Size.X - 22, 22),
                     new Vector2(22, Size.Y - 22), Size - new Vector2(22, 22) })
        {
            float x = corner.X < Size.X / 2 ? 1 : -1;
            float y = corner.Y < Size.Y / 2 ? 1 : -1;
            DrawPolyline(new[] { corner + new Vector2(0, 32 * y), corner + new Vector2(8 * x, 12 * y),
                corner + new Vector2(12 * x, 8 * y), corner + new Vector2(32 * x, 0) }, gold, 1.2f, true);
            Vector2 center = corner + new Vector2(3 * x, 3 * y);
            DrawPolyline(new[] { center + new Vector2(0, -5), center + new Vector2(5, 0),
                center + new Vector2(0, 5), center + new Vector2(-5, 0), center + new Vector2(0, -5) }, gold, 1.2f, true);
        }
    }
}
