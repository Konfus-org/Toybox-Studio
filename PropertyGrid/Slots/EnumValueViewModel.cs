using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>Edits an enum as a dropdown over its values, each option showing its display label and,
/// where the member declares a <c>[Tooltip]</c>, an explanatory tooltip.</summary>
public sealed class EnumValueViewModel : ValueViewModel
{
    public EnumValueViewModel(PropertyValueAccessor accessor, Type enumType)
        : base(accessor)
        => Choices =
        [
            .. Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(value => new EnumChoice(value, value.GetDisplayName(), value.GetTooltip())),
        ];

    public IReadOnlyList<EnumChoice> Choices { get; }

    // Named Value so the base's accessor-change notification (fired as "Value") refreshes the selection.
    public EnumChoice? Value
    {
        get => Choices.FirstOrDefault(choice => Equals(choice.Value, Accessor.Get()));
        set
        {
            // The dropdown clears its selection transiently while its items refresh; only real picks commit.
            if (value is not null)
                Accessor.Set(value.Value);
        }
    }
}
