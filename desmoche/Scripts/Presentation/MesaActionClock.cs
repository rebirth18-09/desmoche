using System;
using Godot;

namespace Desmoche.Presentation;

// Solo dibuja el plazo recibido. No resuelve vencimientos ni acciones.
public partial class MesaActionClock : Control
{
    private Label _number = null!;
    private float _fraction = 1;
    public int SecondsRemaining { get; private set; }
    public float RemainingFraction => _fraction;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = Size = new Vector2(44, 44);
        _number = new Label { Name = "Seconds", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _number.AddThemeFontSizeOverride("font_size", 18);
        _number.AddThemeColorOverride("font_color", new Color(.98f, .85f, .58f));
        AddChild(_number); _number.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += QueueRedraw;
    }

    public void SetRemaining(int milliseconds, int duration, string caption)
    {
        int seconds = (Math.Max(0, milliseconds) + 999) / 1000;
        float fraction = Math.Clamp(milliseconds / (float)Math.Max(1, duration), 0, 1);
        bool redraw = seconds != SecondsRemaining || Math.Abs(fraction - _fraction) >= .001f;
        SecondsRemaining = seconds;
        if (redraw) { _fraction = fraction; QueueRedraw(); }
        if (_number is not null) _number.Text = seconds.ToString();
        TooltipText = caption + ": " + seconds + " s";
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2; float radius = Math.Min(Size.X, Size.Y) / 2 - 3;
        DrawCircle(center + new Vector2(0, 2), radius + 1, new Color(0, 0, 0, .3f));
        DrawCircle(center, radius, new Color(.018f, .045f, .047f, .98f));
        DrawArc(center, radius, 0, Mathf.Tau, 64, new Color(.45f, .34f, .15f), 2, true);
        if (_fraction > 0)
            DrawArc(center, radius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _fraction, 64,
                SecondsRemaining <= 3 ? new Color(.96f, .56f, .26f) : new Color(.94f, .76f, .38f), 3, true);
    }
}
