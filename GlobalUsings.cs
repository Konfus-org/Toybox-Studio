// Project-wide alias for the editor's strong icon type, so call sites read Icon.Pencil without importing
// IconPacks everywhere. Icon is the IconPacks Lucide kind enum (1600+ icons; Icon.None means "no
// icon"). Using it as the icon type means an unknown glyph is a compile error, not a silent runtime miss.
//
// Colours are NOT aliased: they are plain Avalonia Color values authored from the named consts in
// Toybox.Studio.Utils.Colors (Colors.Red, Colors.Blue, …) — the single source for the editor's palette.
global using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;
