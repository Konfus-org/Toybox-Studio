using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// The wire codec for an <see cref="InputMap"/>'s schemes, speaking the engine's canonical asset
/// shape: every field a <c>{type, value}</c> envelope, a binding's control the double-wrapped
/// variant (<c>{type: "variant", value: {type: &lt;alternative&gt;, value}}</c>), keys snake_case,
/// enums written numerically (the engine reader accepts names and numbers; names can't be mangled
/// between the two languages' spellings). Reads are lenient — envelopes are unwrapped wherever they
/// appear and malformed tokens yield defaults. <see cref="ReadDocument"/>/<see cref="WriteDocument"/>
/// round-trip a whole <c>.inputmap</c> file body, which is how the editor's own keybindings persist
/// without an engine round-trip.
/// </summary>
public sealed class InputSchemeListConverter : IWireConverter<IReadOnlyList<InputScheme>>
{
    public IReadOnlyList<InputScheme> Read(JToken value) => ReadSchemes(value);

    public JToken Write(IReadOnlyList<InputScheme> value) => WriteSchemes(value);

    /// <summary>Reads a whole <c>.inputmap</c> body: <c>{schemes: {type: "array", value: [...]}}</c>.</summary>
    public static IReadOnlyList<InputScheme> ReadDocument(JObject document) =>
        ReadSchemes(document["schemes"]);

    /// <summary>Writes a whole <c>.inputmap</c> body the engine's asset loader reads back.</summary>
    public static JObject WriteDocument(IReadOnlyList<InputScheme> schemes) => new()
    {
        ["schemes"] = Envelope("array", WriteSchemes(schemes)),
    };

    internal static IReadOnlyList<InputScheme> ReadSchemes(JToken? token) =>
        Unwrap(token) is JArray schemes ? [.. schemes.Select(ReadScheme)] : [];

    internal static JArray WriteSchemes(IEnumerable<InputScheme> schemes) =>
        new(schemes.Select(WriteScheme));

    private static InputScheme ReadScheme(JToken token) =>
        Unwrap(token) is not JObject scheme
            ? new InputScheme()
            : new InputScheme
            {
                Name = WireValue.ReadString(Unwrap(scheme["name"])),
                IsActive = WireValue.ReadBool(Unwrap(scheme["is_active"])),
                Actions = Unwrap(scheme["actions"]) is JArray actions
                    ? [.. actions.Select(ReadAction)]
                    : [],
            };

    private static JObject WriteScheme(InputScheme scheme) => new()
    {
        ["name"] = Envelope("string", scheme.Name),
        ["is_active"] = Envelope("bool", scheme.IsActive),
        ["actions"] = Envelope("array", new JArray(scheme.Actions.Select(WriteAction))),
    };

    private static InputAction ReadAction(JToken token) =>
        Unwrap(token) is not JObject action
            ? new InputAction()
            : new InputAction
            {
                Name = WireValue.ReadString(Unwrap(action["name"])),
                ValueType = WireValue.ReadEnum(Unwrap(action["value_type"]), InputActionValueType.Button),
                Bindings = Unwrap(action["bindings"]) is JArray bindings
                    ? [.. bindings.Select(ReadBinding)]
                    : [],
            };

    private static JObject WriteAction(InputAction action) => new()
    {
        ["name"] = Envelope("string", action.Name),
        ["value_type"] = Envelope("input_action_value_type", (int)action.ValueType),
        ["bindings"] = Envelope("array", new JArray(action.Bindings.Select(WriteBinding))),
    };

    private static InputBinding ReadBinding(JToken token) =>
        Unwrap(token) is not JObject binding
            ? new InputBinding()
            : new InputBinding
            {
                Control = ReadControl(binding["control"]),
                Scale = WireValue.ReadSingle(Unwrap(binding["scale"]), 1f),
            };

    private static JObject WriteBinding(InputBinding binding) => new()
    {
        ["control"] = Envelope("variant", WriteControl(binding.Control)),
        ["scale"] = Envelope("float", binding.Scale),
    };

    private static InputControl ReadControl(JToken? token)
    {
        // Descend through the field envelope and the "variant" wrapper to the alternative's own
        // { type, value } node.
        var node = Unwrap(token);
        while (node is JObject wrapper && WireValue.ReadString(wrapper["type"]) == "variant")
            node = wrapper["value"];

        if (node is not JObject alternative)
            return new KeyboardInputControl();

        var fields = alternative["value"] as JObject;
        return WireValue.ReadString(alternative["type"]) switch
        {
            "keyboard_input_control" => new KeyboardInputControl
            {
                Key = ReadEnumField(fields, "key", InputKey.Unknown),
            },
            "key_chord_input_control" => new KeyChordInputControl
            {
                Key = ReadEnumField(fields, "key", InputKey.Unknown),
                Ctrl = ReadBoolField(fields, "ctrl"),
                Shift = ReadBoolField(fields, "shift"),
                Alt = ReadBoolField(fields, "alt"),
                Gui = ReadBoolField(fields, "gui"),
            },
            "mouse_button_input_control" => new MouseButtonInputControl
            {
                Button = ReadEnumField(fields, "button", InputMouseButton.Unknown),
            },
            "mouse_vector_input_control" => new MouseVectorInputControl
            {
                Control = ReadEnumField(fields, "control", InputMouseVectorControl.Delta),
            },
            "mouse_axis_input_control" => new MouseAxisInputControl
            {
                Control = ReadEnumField(fields, "control", InputMouseAxisControl.Wheel),
            },
            "keyboard_vector2_composite_input_control" => new KeyboardVector2CompositeInputControl
            {
                Up = ReadEnumField(fields, "up", InputKey.Unknown),
                Down = ReadEnumField(fields, "down", InputKey.Unknown),
                Left = ReadEnumField(fields, "left", InputKey.Unknown),
                Right = ReadEnumField(fields, "right", InputKey.Unknown),
            },
            "controller_button_input_control" => new ControllerButtonInputControl
            {
                ControllerIndex = WireValue.ReadInt(Unwrap(fields?["controller_index"]), -1),
                Button = ReadEnumField(fields, "button", InputControllerButton.Unknown),
            },
            "controller_axis_input_control" => new ControllerAxisInputControl
            {
                ControllerIndex = WireValue.ReadInt(Unwrap(fields?["controller_index"]), -1),
                Axis = ReadEnumField(fields, "axis", InputControllerAxis.Unknown),
            },
            "controller_stick_input_control" => new ControllerStickInputControl
            {
                ControllerIndex = WireValue.ReadInt(Unwrap(fields?["controller_index"]), -1),
                XAxis = ReadEnumField(fields, "x_axis", InputControllerAxis.Unknown),
                YAxis = ReadEnumField(fields, "y_axis", InputControllerAxis.Unknown),
            },
            _ => new KeyboardInputControl(),
        };
    }

    private static JObject WriteControl(InputControl control) => control switch
    {
        KeyChordInputControl chord => Alternative("key_chord_input_control", new JObject
        {
            ["key"] = Envelope("input_key", (int)chord.Key),
            ["ctrl"] = Envelope("bool", chord.Ctrl),
            ["shift"] = Envelope("bool", chord.Shift),
            ["alt"] = Envelope("bool", chord.Alt),
            ["gui"] = Envelope("bool", chord.Gui),
        }),
        MouseButtonInputControl mouse => Alternative("mouse_button_input_control", new JObject
        {
            ["button"] = Envelope("input_mouse_button", (int)mouse.Button),
        }),
        MouseVectorInputControl vector => Alternative("mouse_vector_input_control", new JObject
        {
            ["control"] = Envelope("input_mouse_vector_control", (int)vector.Control),
        }),
        MouseAxisInputControl axis => Alternative("mouse_axis_input_control", new JObject
        {
            ["control"] = Envelope("input_mouse_axis_control", (int)axis.Control),
        }),
        KeyboardVector2CompositeInputControl composite =>
            Alternative("keyboard_vector2_composite_input_control", new JObject
            {
                ["up"] = Envelope("input_key", (int)composite.Up),
                ["down"] = Envelope("input_key", (int)composite.Down),
                ["left"] = Envelope("input_key", (int)composite.Left),
                ["right"] = Envelope("input_key", (int)composite.Right),
            }),
        ControllerButtonInputControl button => Alternative("controller_button_input_control", new JObject
        {
            ["controller_index"] = Envelope("int", button.ControllerIndex),
            ["button"] = Envelope("input_controller_button", (int)button.Button),
        }),
        ControllerAxisInputControl axis => Alternative("controller_axis_input_control", new JObject
        {
            ["controller_index"] = Envelope("int", axis.ControllerIndex),
            ["axis"] = Envelope("input_controller_axis", (int)axis.Axis),
        }),
        ControllerStickInputControl stick => Alternative("controller_stick_input_control", new JObject
        {
            ["controller_index"] = Envelope("int", stick.ControllerIndex),
            ["x_axis"] = Envelope("input_controller_axis", (int)stick.XAxis),
            ["y_axis"] = Envelope("input_controller_axis", (int)stick.YAxis),
        }),
        KeyboardInputControl keyboard => Alternative("keyboard_input_control", new JObject
        {
            ["key"] = Envelope("input_key", (int)keyboard.Key),
        }),
        _ => Alternative("keyboard_input_control", new JObject
        {
            ["key"] = Envelope("input_key", (int)InputKey.Unknown),
        }),
    };

    private static TEnum ReadEnumField<TEnum>(JObject? fields, string key, TEnum fallback)
        where TEnum : struct, Enum =>
        WireValue.ReadEnum(Unwrap(fields?[key]), fallback);

    private static bool ReadBoolField(JObject? fields, string key) =>
        WireValue.ReadBool(Unwrap(fields?[key]));

    // A field written by the engine is a { type, value } envelope; a bare value reads as itself.
    // No wire shape here has a real field named "value", so the probe can't misfire.
    private static JToken? Unwrap(JToken? token) =>
        token is JObject envelope && envelope["value"] is { } value ? value : token;

    private static JObject Envelope(string type, JToken value) => new()
    {
        ["type"] = type,
        ["value"] = value,
    };

    private static JObject Alternative(string typeName, JObject fields) =>
        Envelope(typeName, fields);
}
