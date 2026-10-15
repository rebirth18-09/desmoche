using System;
using Godot;

namespace Desmoche.Presentation;

public partial class Main
{
    private bool _mobileDisplay;
    private double _insetsElapsed;

    private void InitializeResponsiveWindow()
    {
        _mobileDisplay = OS.HasFeature("android") || OS.HasFeature("ios");
        GetViewport().SizeChanged += UpdateSafeMargins;
        UpdateSafeMargins();
    }

    private void UpdateMobileInsets(double delta)
    {
        if (!_mobileDisplay) return;
        _insetsElapsed += delta;
        if (_insetsElapsed < 0.25) return;
        _insetsElapsed = 0;
        // Girar 180 grados o abrir el teclado puede cambiar el área segura
        // sin cambiar el tamaño del viewport. Solo se consulta en móviles.
        UpdateSafeMargins();
    }

    private void UpdateSafeMargins()
    {
        var margin = GetNode<MarginContainer>("Margin");
        Vector2 viewportSize = GetViewportRect().Size;
        float left = 12, top = 12, right = 12, bottom = 12;
        if (_mobileDisplay)
        {
            Transform2D transform = GetViewport().GetScreenTransform();
            Rect2 safe = DisplayServer.GetDisplaySafeArea();
            if (safe.Size.X > 0 && safe.Size.Y > 0)
            {
                Rect2 local = transform.AffineInverse() * safe;
                left += Math.Max(0, local.Position.X);
                top += Math.Max(0, local.Position.Y);
                right += Math.Max(0, viewportSize.X - local.End.X);
                bottom += Math.Max(0, viewportSize.Y - local.End.Y);
            }
            float keyboard = DisplayServer.VirtualKeyboardGetHeight() / Math.Max(0.01f, transform.Y.Length());
            bottom = Math.Max(bottom, keyboard + 12);
        }
        SetMargin("left", left); SetMargin("top", top);
        SetMargin("right", right); SetMargin("bottom", bottom);
        void SetMargin(string side, float value)
        {
            string key = "margin_" + side;
            int pixels = (int)Math.Ceiling(value);
            if (margin.GetThemeConstant(key) != pixels) margin.AddThemeConstantOverride(key, pixels);
        }
    }
}
