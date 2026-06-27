using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared parent for the reference pickers — the asset/handle picker
/// (<see cref="HandlePickerPropertyViewModel"/>) and the entity picker
/// (<see cref="EntityPickerPropertyViewModel"/>). Both render identically (a type icon button plus the
/// referenced item's name as a link) and behave identically: clicking an empty ("None") reference opens the
/// chooser, and committing a pick writes the chosen id back to the backing token. Subclasses supply the icon,
/// the chooser's title/options, name resolution, and what (if anything) clicking a *set* reference does.
/// </summary>
public abstract partial class PickerPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    [ObservableProperty]
    private string _displayName = "None";

    protected PickerPropertyViewModel(PropertyNode node, Action? commit) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        CommitChanges = commit;
    }

    /// <summary>Lucide icon shown in the picker button (e.g. "Target" for assets, "Search" for entities).</summary>
    public abstract string IconName { get; }

    /// <summary>Tooltip for the picker button and the name link.</summary>
    public abstract string PickTooltip { get; }

    // Ids (asset handles, entity ids) are UNSIGNED 64-bit: read as ulong so a high-bit id survives rather
    // than overflowing a signed read.
    public ulong CurrentId => _slot.Read<ulong?>() ?? 0;

    public bool HasReference => CurrentId != 0;

    // A reference is just its id. Read it from the live slot (the base field goes stale once a pick replaces
    // the token), and accept any integer token on paste — the same shape Copy writes — so a "model" handle
    // copies and pastes like any other property.
    public override JToken? CurrentValue => new JValue(CurrentId);

    public override void ApplyValue(JToken token)
    {
        var id = token.Type == JTokenType.Integer ? token.Value<ulong>() : 0UL;
        if (_slot.Set(new JValue(id)))
        {
            RefreshDisplay();
            RaiseCommit();
        }
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
        var (title, options) = BuildChoices();
        // The chooser passes ids as signed long; reinterpret the full 64 bits both ways so a high-bit
        // (above long.MaxValue) unsigned id survives the round-trip rather than overflowing.
        var pick = await AssetPicker
            .ShowAsync(title, options, unchecked((long)CurrentId))
            .ContinueOnSameContext();
        if (!pick.Confirmed)
            return;

        if (_slot.Set(new JValue(unchecked((ulong)pick.Id))))
        {
            RefreshDisplay();
            RaiseCommit();
        }
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
    protected abstract (string Title, IReadOnlyList<AssetInfo> Options) BuildChoices();

    /// <summary>Whether clicking a *set* reference navigates somewhere (default: no — re-open the chooser).</summary>
    protected virtual bool RevealsOnActivate => false;

    /// <summary>Navigates to a set reference; only called when <see cref="RevealsOnActivate"/> is true.</summary>
    protected virtual void Reveal() { }
}
