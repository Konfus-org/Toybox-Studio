// The script editor's colour themes — ONE place to edit how code is coloured.
//
// Both editors colour code from two sources, and this file drives both with the same palette:
//   • C++  — clangd's semantic tokens (namespace / class / struct / type / function / …). Monaco resolves each
//            to a theme rule by its bare name (it matches "class.declaration" etc. against the rule trie), so a
//            rule named "class" colours every C++ class.
//   • GLSL — the Monarch grammar in languages.js, whose token names ("type", "predefined", …) the rules below
//            also cover. GLSL has no language server, so it can only distinguish the built-in types/functions
//            the grammar knows; C++ gets full struct/class/namespace resolution from clangd.
//
// TO RETHEME: edit the colour hex values in DARK / LIGHT below (no "#"). The keys are categories, not token
// names — `rules()` maps each token/semantic kind onto a category, so changing one colour restyles everything
// in that category across both languages. editor.js registers these and selects toybox-dark / toybox-light to
// follow the Studio light/dark theme.
(function () {
  "use strict";

  // ===== EDIT HERE: dark theme palette (on a ~#1e1e1e background) =====
  var DARK = {
    background: "1E1E1E",
    foreground: "D4D4D4",
    comment: "6A9955",   // comments
    keyword: "C586C0",   // language keywords (if, for, const, layout, return, …)
    string: "CE9178",    // string literals
    number: "B5CEA8",    // numeric literals
    operator: "D4D4D4",  // + - * / = . etc.
    basicType: "4EC9B0", // BUILT-IN types: int, float, vec3, mat4, sampler2D, bool, … (teal)
    userType: "E5C07B",  // STRUCT / CLASS / interface / enum — user-defined aggregate types (gold)
    namespace: "4FC1FF", // namespaces / modules (bright blue)
    function: "DCDCAA",  // functions & methods (pale yellow)
    macro: "BD93F9",     // macros & GLSL preprocessor (#version, #define) (lavender)
    variable: "9CDCFE",  // variables, parameters, properties (light blue)
    constant: "D7BA7D",  // enum members & built-in vars (gl_Position) (tan)
  };

  // ===== EDIT HERE: light theme palette (on a white background) =====
  var LIGHT = {
    background: "FFFFFF",
    foreground: "1E1E1E",
    comment: "008000",
    keyword: "AF00DB",
    string: "A31515",
    number: "098658",
    operator: "1E1E1E",
    basicType: "267F99", // built-in types (teal)
    userType: "8A6D00",  // struct / class / interface / enum (dark gold)
    namespace: "0070C1", // namespaces (blue)
    function: "795E26",  // functions & methods (brown)
    macro: "8000FF",     // macros & preprocessor (violet)
    variable: "001080",  // variables / parameters / properties (dark blue)
    constant: "8C5A00",  // enum members & built-in vars
  };

  // Maps every token kind (both clangd semantic types and GLSL Monarch tokens) onto a palette colour. Monaco
  // matches by dotted prefix, so "type" also catches "type.declaration", "string" catches "string.escape", etc.
  function rules(p) {
    return [
      // Literals & punctuation (shared by both languages).
      { token: "comment", foreground: p.comment, fontStyle: "italic" },
      { token: "string", foreground: p.string },
      { token: "number", foreground: p.number },
      { token: "operator", foreground: p.operator },
      { token: "delimiter", foreground: p.operator },
      { token: "keyword", foreground: p.keyword },

      // Types — the headline distinction the editor is meant to make obvious at a glance:
      { token: "type", foreground: p.basicType },           // built-in types (GLSL grammar + clangd "type")
      { token: "typeParameter", foreground: p.basicType },  // template/type params
      { token: "class", foreground: p.userType },           // C++ classes (clangd semantic)
      { token: "struct", foreground: p.userType },          // C++ structs (clangd semantic)
      { token: "interface", foreground: p.userType },
      { token: "enum", foreground: p.userType },
      { token: "namespace", foreground: p.namespace },      // C++ namespaces (clangd semantic)

      // Callables & values.
      { token: "function", foreground: p.function },
      { token: "method", foreground: p.function },
      { token: "predefined", foreground: p.function },          // GLSL built-in functions (texture, normalize…)
      { token: "macro", foreground: p.macro },
      { token: "keyword.directive", foreground: p.macro },      // GLSL preprocessor (#version, #ifdef…)
      { token: "variable", foreground: p.variable },
      { token: "parameter", foreground: p.variable, fontStyle: "italic" },
      { token: "property", foreground: p.variable },
      { token: "enumMember", foreground: p.constant },
      { token: "variable.predefined", foreground: p.constant }, // GLSL built-in vars (gl_Position…)
    ];
  }

  function define(monaco, name, base, p) {
    monaco.editor.defineTheme(name, {
      base: base,
      inherit: true,           // keep the base theme's editor chrome (gutter, selection, etc.)
      rules: rules(p),
      colors: {
        "editor.background": "#" + p.background,
        "editor.foreground": "#" + p.foreground,
      },
    });
  }

  window.__tbxThemes = {
    dark: "toybox-dark",
    light: "toybox-light",
    // Called once from editor.js after editor.main loads, before the first model is shown.
    register: function (monaco) {
      define(monaco, "toybox-dark", "vs-dark", DARK);
      define(monaco, "toybox-light", "vs", LIGHT);
    },
  };
})();
