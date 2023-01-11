#region

using System.Windows;

#endregion

namespace AutopilotQuick.SnakeGame;

public class SnakePart
{
    public UIElement UiElement { get; set; }

    public Point Position { get; set; }

    public bool IsHead { get; set; }
}