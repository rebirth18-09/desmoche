using Godot;

namespace Desmoche.Presentation;

/// <summary>Coloca las dos tarjetas, conservando las rutas de los botones del lobby.</summary>
public partial class MultiplayerChoices : Control
{
    private Control _createCard = null!, _joinCard = null!;
    private Button _create = null!, _join = null!, _back = null!;
    private bool _ready;

    public override void _Ready()
    {
        _createCard = GetNode<Control>("CreateCard");
        _joinCard = GetNode<Control>("JoinCard");
        _create = GetNode<Button>("CreateOption");
        _join = GetNode<Button>("JoinOption");
        _back = GetNode<Button>("BackButton");
        Resized += Arrange;
        _ready = true;
        Arrange();
    }

    private void Arrange()
    {
        if (!_ready) return;
        bool stacked = Size.X < 650;
        float width = stacked ? Size.X : (Size.X - 24) / 2;
        float height = stacked ? 246 : 286;
        _createCard.Position = Vector2.Zero; _createCard.Size = new Vector2(width, height);
        _joinCard.Position = stacked ? new Vector2(0, height + 18) : new Vector2(width + 24, 0);
        _joinCard.Size = new Vector2(width, height);
        _create.Position = _createCard.Position + new Vector2(24, height - 84);
        _join.Position = _joinCard.Position + new Vector2(24, height - 84);
        _create.Size = _join.Size = new Vector2(width - 48, 58);
        float end = stacked ? height * 2 + 18 : height;
        _back.Position = new Vector2(Size.X / 2 - 130, end + 18); _back.Size = new Vector2(260, 42);
        CustomMinimumSize = new Vector2(0, end + 64);
    }

    public override void _ExitTree()
    {
        Resized -= Arrange;
        _ready = false;
    }
}
