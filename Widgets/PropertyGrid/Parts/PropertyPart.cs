using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Base for a "PropertyPart" — one composable piece of a property-grid row (the drag handle, the add/remove
/// affordances, the disclosure chevron, the state indicator). A <see cref="PropertyViewModel"/> is composed of
/// these parts, and the shared <see cref="PropertyRow"/> chrome lays each out in its own slot. Each concrete
/// part is its own view-model type so it pairs with its own View via a DataTemplate in PropertyGridView.axaml.
/// </summary>
public abstract class PropertyPart : ObservableObject;
