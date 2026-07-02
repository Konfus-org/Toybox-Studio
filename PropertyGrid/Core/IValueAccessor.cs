namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The live value cursor a property widget edits — the reflection-native successor to the old
/// <c>PropertyNode.Value</c> + <c>JsonValueSlot</c>. Widgets read and write <b>CLR values</b> through this
/// interface and never touch JSON: a number editor <see cref="Set"/>s an <c>int</c>/<c>float</c>, a colour editor
/// a <see cref="Avalonia.Media.Color"/>, a vector editor a <c>Vector3</c>, a list editor mutates the
/// <see cref="System.Collections.IList"/> returned by <see cref="Get"/>. JSON is produced only at the commit
/// boundary. Two implementations back it: <see cref="ReflectedAccessor"/> binds a typed C# member (edits push
/// through the reflected object's generated setter), and <see cref="JsonAccessor"/> binds a live <c>JToken</c>
/// for engine-authored data the editor can't model.
/// </summary>
public interface IValueAccessor
{
    /// <summary>The declared CLR type of the value (e.g. <c>float</c>, <c>Vector3</c>, <c>AssetHandle</c>,
    /// <c>List&lt;Lod&gt;</c>). For a JSON-backed composite with no CLR type, a <c>JObject</c>/<c>JArray</c>
    /// marker — routing then goes by the descriptor's type token, not this.</summary>
    Type ValueType { get; }

    /// <summary>False for a read-only value: editors disable their control and never <see cref="Set"/>.</summary>
    bool CanWrite { get; }

    /// <summary>The current value as a CLR object.</summary>
    object? Get();

    /// <summary>Writes a CLR value back into the model. Does not itself push to the engine — call
    /// <see cref="Commit"/> after.</summary>
    void Set(object? value);

    /// <summary>Raises the owning property's commit (engine push / buffered dirty). A no-op for a value with no
    /// commit (a read-only or preview row). Suppressed by the widget base during a <c>Sync</c>.</summary>
    void Commit();

    /// <summary>An accessor for a nested struct/object member, by its descriptor. Only meaningful on an object
    /// accessor.</summary>
    IValueAccessor Member(PropertyDescriptor member);

    /// <summary>An accessor for a list element by index. Only meaningful on an array accessor.</summary>
    IValueAccessor Element(int index);

    /// <summary>A fresh default element for a list — a typed default (<c>Activator.CreateInstance</c>) or a cloned
    /// JSON template. Null on a non-array accessor.</summary>
    object? CreateElement();
}
