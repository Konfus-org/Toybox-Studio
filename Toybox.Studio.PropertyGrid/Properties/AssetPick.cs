namespace Toybox.Studio.PropertyGrid;

/// <summary>The outcome of an asset/entity picker: whether the user confirmed a choice, and the chosen id.</summary>
public readonly record struct AssetPick(bool Confirmed, ulong Id);
