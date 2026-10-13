using System;
using System.Collections.Generic;
using Godot;

namespace Desmoche.Presentation;

/// <summary>Selección exclusiva visible. No crea partidas ni interpreta reglas.</summary>
public partial class MenuChoiceSelector : HBoxContainer
{
    [Signal] public delegate void ItemSelectedEventHandler(long index);
    [Export] public bool BotCount { get; set; }
    private readonly List<Button> _buttons = new();
    private readonly List<Action> _handlers = new();
    private int _selected;
    public int ItemCount => _buttons.Count;
    public int Selected => _selected;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 12);
        Texture2D person = GD.Load<Texture2D>("res://Assets/UI/Salon/Person.svg");
        string[] levels = { "Fácil", "Medio", "Difícil", "Experto" };
        int count = BotCount ? 3 : levels.Length;
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var button = new Button
            {
                Name = "Choice" + i, ToggleMode = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(BotCount ? 88 : 82, BotCount ? 112 : 48),
                ThemeTypeVariation = BotCount ? "BotChoice" : "DifficultyChoice",
                Text = BotCount ? "" : levels[i],
                TooltipText = BotCount ? (i + 1) + (i == 0 ? " bot · " : " bots · ") + (i + 2) + " jugadores" : levels[i]
            };
            AddChild(button);
            if (BotCount)
            {
                var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                button.AddChild(content);
                content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                content.OffsetTop = 13; content.OffsetBottom = -10;
                content.AddThemeConstantOverride("separation", 3);
                var icons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
                icons.AddThemeConstantOverride("separation", 3);
                content.AddChild(icons);
                for (int n = 0; n <= i; n++) icons.AddChild(new TextureRect
                {
                    Texture = person, CustomMinimumSize = new Vector2(24, 28),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore
                });
                var title = new Label { Text = (i + 1) + (i == 0 ? " bot" : " bots"), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                title.AddThemeFontSizeOverride("font_size", 22); content.AddChild(title);
                var total = new Label { Text = (i + 2) + " jugadores", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                total.AddThemeFontSizeOverride("font_size", 18); content.AddChild(total);
            }
            Action handler = () =>
            {
                Select(index);
                EmitSignal(SignalName.ItemSelected, index);
            };
            _buttons.Add(button); _handlers.Add(handler); button.Pressed += handler;
        }
        Select(BotCount ? 0 : 1);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= ItemCount) throw new ArgumentOutOfRangeException(nameof(index));
        _selected = index;
        for (int i = 0; i < ItemCount; i++)
        {
            _buttons[i].SetPressedNoSignal(i == index);
            _buttons[i].AddThemeColorOverride("font_focus_color", i == index && !BotCount
                ? new Color(0.025f, 0.13f, 0.085f) : new Color(0.98f, 0.94f, 0.80f));
            foreach (Node child in _buttons[i].GetChildren())
                if (child is Control control) control.Modulate = i == index && BotCount
                    ? new Color(1, 0.91f, 0.61f) : Colors.White;
        }
    }

    public int GetSelectedId() => BotCount ? _selected + 1 : _selected;
    public string GetItemText(int index) => _buttons[index].TooltipText;
    public Button GetChoice(int index) => _buttons[index];
    public override void _ExitTree()
    {
        for (int i = 0; i < _buttons.Count; i++) _buttons[i].Pressed -= _handlers[i];
    }
}
