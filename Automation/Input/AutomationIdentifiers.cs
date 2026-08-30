// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

namespace AutoClicker;

/// <summary>Stable identifiers persisted for actions and custom-sequence events.</summary>
public static class AutomationInputIds
{
    public const string Unset = "Unset";
    public const string Left = "Left";
    public const string Right = "Right";
    public const string Middle = "Middle";
    public const string Mouse4 = "Mouse4";
    public const string Mouse5 = "Mouse5";
    public const string ScrollUp = "ScrollUp";
    public const string ScrollDown = "ScrollDown";
    public const string ScrollLeft = "ScrollLeft";
    public const string ScrollRight = "ScrollRight";
    public const string Space = "Space";
    public const string Enter = "Enter";
    public const string Custom = "Custom";
    public const string Sequence = "Sequence";
    public const string Delay = "Delay";
}

/// <summary>Stable identifiers persisted for action execution modes.</summary>
public static class AutomationActionTypeIds
{
    public const string Single = "Single";
    public const string Double = "Double";
    public const string Hold = "Hold";
    public const string WhileHeld = "While held";
}

/// <summary>Shared labels used when presenting persisted input identifiers.</summary>
public static class AutomationInputLabels
{
    public const string SetAction = "Set action";
    public const string LeftClick = "Left click";
    public const string RightClick = "Right click";
    public const string MiddleClick = "Middle click";
    public const string Mouse4Click = "Mouse 4 click";
    public const string Mouse5Click = "Mouse 5 click";
    public const string ScrollUp = "Scroll up";
    public const string ScrollDown = "Scroll down";
    public const string ScrollLeft = "Scroll left";
    public const string ScrollRight = "Scroll right";
    public const string CustomSequence = "Custom sequence";
    public const string Wait = "Wait";
    public const string LeftMouse = "left mouse";
    public const string RightMouse = "right mouse";
    public const string MiddleMouse = "middle mouse";
    public const string Mouse4 = "Mouse 4";
    public const string Mouse5 = "Mouse 5";
}

public static class AutomationProfileNames
{
    public const string Default = "General";
    public const string New = "New profile";
    public const string Fallback = "Profile";
}
