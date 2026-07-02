using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Shared parent for the reference pickers — the asset/handle picker
/// (<see cref="HandlePickerPropertyViewModel"/>) and the entity picker
/// (<see cref="EntityPickerPropertyViewModel"/>). Both render identically (a type icon button plus the
/// referenced item's name as a link) and behave identically: clicking an empty ("None") reference opens the
/// chooser, and committing a pick writes the chosen id back through the accessor (as an
/// <see cref="AssetHandle"/>). Subclasses supply the icon, the chooser's title/options, name resolution, and
/// what (if anything) clicking a *set* reference does.
/// </summary>
public abstract partial class PickerPropertyViewModel : PropertyViewModel
{
    [ObservableProperty]
    private string _displayName = "None";

    protected PickerPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
    }

    /// <summary>Lucide icon shown in the picker button (e.g. Target for assets, Search for entities).</summary>
    public abstract Icon IconName { get; }

    /// <summary>Tooltip for the picker button and the name link.</summary>
    public abstract string PickTooltip { get; }

    // Ids (asset handles, entity ids) are UNSIGNED 64-bit; read the accessor's handle value for the current id.
    public ulong CurrentId => (Accessor.Get() as AssetHandle?)?.Id ?? 0;

    public bool HasReference => CurrentId != 0;

    // A reference is just its id — accept any integer token on paste (the same shape Copy writes), so a "model"
    // handle copies and pastes like any other property.
    public override void ApplyValue(JToken token)
    {
        var id = token.Type == JTokenType.Integer ? token.Value<ulong>() : 0UL;
        SetId(id);
    }

    /// <summary>
    /// Clicking the name link: a set reference activates — pickers with somewhere to go override
    /// <see cref="RevealsOnActivate"/> and <see cref="Reveal"/> (an asset reveals in the OS file explorer);
    /// an empty "None" reference, or a picker with no navigation target, opens the chooser to pick one.
    /// </summary>
    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (HasReference && RevealsOnActivate)
            Reveal();
        else
            await PickAsync().ContinueOnSameContext();
    }

    /// <summary>Opens the modal chooser filtered to this reference's options, then commits the pick.</summary>
    [RelayCommand]
    private async Task PickAsync()
    {
        if (PropertyViewRegistry.AssetPicker is not { } picker)
            return;

        var (title, options) = BuildChoices();
        var pick = await picker
            .ShowAsync(title, options, CurrentId)
            .ContinueOnSameContext();
        if (!pick.Confirmed)
            return;

        SetId(pick.Id);
    }

    // Writes the chosen id through the accessor (as an AssetHandle), refreshes the display, and commits.
    private void SetId(ulong id)
    {
        Accessor.Set(AssetHandle.FromId(id));
        RefreshDisplay();
        RaiseCommit();
    }

    /// <summary>Re-resolves the displayed name and re-evaluates <see cref="HasReference"/>.</summary>
    protected void RefreshDisplay()
    {
        DisplayName = ResolveDisplayName();
        OnPropertyChanged(nameof(HasReference));
    }

    /// <summary>The display name for the current reference, or "None" when unset.</summary>
    protected abstract string ResolveDisplayName();

    /// <summary>The chooser title plus the options to present for this reference type.</summary>
    protected abstract (string Title, IReadOnlyList<AssetMeta> Options) BuildChoices();

    /// <summary>Whether clicking a *set* reference navigates somewhere (default: no — re-open the chooser).</summary>
    protected virtual bool RevealsOnActivate => false;

    /// <summary>Navigates to a set reference; only called when <see cref="RevealsOnActivate"/> is true.</summary>
    protected virtual void Reveal() { }
}
