namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// One selectable option in an <see cref="EnumValueViewModel"/> dropdown: the boxed enum value, the
/// label shown for it (its <c>[DisplayName]</c> or humanized member name), and an optional tooltip
/// (its <c>[Tooltip]</c>) explaining what the option does.
/// </summary>
public sealed record EnumChoice(object Value, string Label, string? Tooltip);
