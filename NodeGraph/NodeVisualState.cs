namespace Toybox.Studio.NodeGraph;

/// <summary>
/// How a node presents in the selection-focused overlay: <see cref="Hidden"/> off entirely, <see cref="Plug"/>
/// a small faded-in connector square (an entity connected to the selection), or <see cref="Open"/> the full
/// inspector card (a selected entity). Only the selection's nodes open; their referenced neighbours surface as
/// plugs, and clicking a plug selects it — opening it and walking the focus to it.
/// </summary>
public enum NodeVisualState
{
    Hidden,
    Plug,
    Open,
}
