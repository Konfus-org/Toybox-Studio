// Custom Monaco languages that aren't in the vendored basic-languages bundle. Today that's just GLSL; add
// another the same way (register the id + a Monarch grammar) and list its extensions in the C# ScriptLanguages
// table so files resolve to it. editor.js calls register(monaco) once, after editor.main has loaded and before
// any model is created, so a model opened as "glsl" already has its grammar.
(function () {
  "use strict";

  var registered = Object.create(null);

  function registerGlsl(monaco) {
    monaco.languages.register({
      id: "glsl",
      extensions: [".glsl", ".frag", ".vert", ".comp", ".geo", ".geom", ".tesc", ".tese"],
      aliases: ["GLSL", "glsl"]
    });

    monaco.languages.setLanguageConfiguration("glsl", {
      comments: { lineComment: "//", blockComment: ["/*", "*/"] },
      brackets: [["{", "}"], ["[", "]"], ["(", ")"]],
      autoClosingPairs: [
        { open: "{", close: "}" }, { open: "[", close: "]" },
        { open: "(", close: ")" }, { open: '"', close: '"' }
      ],
      surroundingPairs: [
        { open: "{", close: "}" }, { open: "[", close: "]" },
        { open: "(", close: ")" }, { open: '"', close: '"' }
      ]
    });

    monaco.languages.setMonarchTokensProvider("glsl", {
      defaultToken: "",
      tokenPostfix: ".glsl",

      keywords: [
        "attribute", "const", "uniform", "varying", "buffer", "shared", "coherent", "volatile",
        "restrict", "readonly", "writeonly", "layout", "centroid", "flat", "smooth", "noperspective",
        "patch", "sample", "break", "continue", "do", "for", "while", "switch", "case", "default",
        "if", "else", "subroutine", "in", "out", "inout", "true", "false", "invariant", "precise",
        "discard", "return", "struct", "precision", "highp", "mediump", "lowp"
      ],

      types: [
        "void", "bool", "int", "uint", "float", "double",
        "vec2", "vec3", "vec4", "dvec2", "dvec3", "dvec4",
        "bvec2", "bvec3", "bvec4", "ivec2", "ivec3", "ivec4", "uvec2", "uvec3", "uvec4",
        "mat2", "mat3", "mat4", "mat2x2", "mat2x3", "mat2x4", "mat3x2", "mat3x3", "mat3x4",
        "mat4x2", "mat4x3", "mat4x4",
        "dmat2", "dmat3", "dmat4",
        "sampler1D", "sampler2D", "sampler3D", "samplerCube",
        "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow",
        "sampler1DArray", "sampler2DArray", "sampler2DRect", "samplerBuffer",
        "sampler2DMS", "sampler2DMSArray",
        "isampler1D", "isampler2D", "isampler3D", "isamplerCube",
        "usampler1D", "usampler2D", "usampler3D", "usamplerCube",
        "image1D", "image2D", "image3D", "imageCube", "imageBuffer",
        "atomic_uint"
      ],

      builtinFunctions: [
        "radians", "degrees", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh",
        "pow", "exp", "log", "exp2", "log2", "sqrt", "inversesqrt",
        "abs", "sign", "floor", "trunc", "round", "roundEven", "ceil", "fract", "mod", "modf",
        "min", "max", "clamp", "mix", "step", "smoothstep", "isnan", "isinf",
        "length", "distance", "dot", "cross", "normalize", "faceforward", "reflect", "refract",
        "matrixCompMult", "outerProduct", "transpose", "determinant", "inverse",
        "lessThan", "lessThanEqual", "greaterThan", "greaterThanEqual", "equal", "notEqual", "any", "all", "not",
        "texture", "textureProj", "textureLod", "textureOffset", "texelFetch", "textureGrad",
        "textureSize", "textureGather", "texture2D", "textureCube", "texture2DProj",
        "dFdx", "dFdy", "fwidth", "imageLoad", "imageStore", "imageSize",
        "barrier", "memoryBarrier", "atomicAdd", "atomicAnd", "atomicOr", "atomicXor",
        "atomicMin", "atomicMax", "atomicExchange", "atomicCompSwap",
        "packHalf2x16", "unpackHalf2x16", "floatBitsToInt", "floatBitsToUint", "intBitsToFloat", "uintBitsToFloat"
      ],

      builtinVariables: [
        "gl_Position", "gl_PointSize", "gl_FragCoord", "gl_FragDepth", "gl_FrontFacing",
        "gl_VertexID", "gl_InstanceID", "gl_PrimitiveID", "gl_Layer", "gl_ClipDistance",
        "gl_PointCoord", "gl_GlobalInvocationID", "gl_LocalInvocationID", "gl_WorkGroupID",
        "gl_WorkGroupSize", "gl_NumWorkGroups", "gl_LocalInvocationIndex"
      ],

      operators: [
        "=", ">", "<", "!", "~", "?", ":", "==", "<=", ">=", "!=", "&&", "||", "++", "--",
        "+", "-", "*", "/", "&", "|", "^", "%", "<<", ">>", "+=", "-=", "*=", "/=", "&=", "|=", "^=", "%=", "<<=", ">>="
      ],

      symbols: /[=><!~?:&|+\-*\/\^%]+/,
      escapes: /\\(?:[abfnrtv\\"']|x[0-9A-Fa-f]+|[0-7]+)/,

      tokenizer: {
        root: [
          // Preprocessor directives (#version, #define, #ifdef, ...).
          [/^\s*#\s*\w+/, "keyword.directive"],

          // `struct Name` — colour the declared type name as a user type (the "struct" colour), so it reads
          // distinctly from a built-in type the way clangd colours C++ structs. (Usages can't be resolved
          // without a language server, so only the declaration is highlighted.)
          [/\b(struct)(\s+)([a-zA-Z_]\w*)/, ["keyword", "", "struct"]],

          // An identifier immediately followed by "(" is a call/constructor: a known type stays a type (it's a
          // constructor like vec3(...)), a built-in keeps its built-in colour, anything else reads as a function.
          [/[a-zA-Z_]\w*(?=\s*\()/, {
            cases: {
              "@types": "type",
              "@keywords": "keyword",
              "@builtinFunctions": "predefined",
              "@default": "function"
            }
          }],

          [/[a-zA-Z_]\w*/, {
            cases: {
              "@types": "type",
              "@keywords": "keyword",
              "@builtinFunctions": "predefined",
              "@builtinVariables": "variable.predefined",
              "@default": "identifier"
            }
          }],

          { include: "@whitespace" },

          [/[{}()\[\]]/, "@brackets"],
          [/@symbols/, { cases: { "@operators": "operator", "@default": "" } }],

          // Numbers (float with exponent/suffix, hex, decimal).
          [/\d*\.\d+([eE][\-+]?\d+)?[fFlL]?/, "number.float"],
          [/\d+[eE][\-+]?\d+[fFlL]?/, "number.float"],
          [/0[xX][0-9a-fA-F]+[uU]?/, "number.hex"],
          [/\d+[uUlL]*/, "number"],

          [/[;,.]/, "delimiter"],

          [/"/, { token: "string.quote", bracket: "@open", next: "@string" }]
        ],

        whitespace: [
          [/[ \t\r\n]+/, ""],
          [/\/\*/, "comment", "@comment"],
          [/\/\/.*$/, "comment"]
        ],

        comment: [
          [/[^\/*]+/, "comment"],
          [/\*\//, "comment", "@pop"],
          [/[\/*]/, "comment"]
        ],

        string: [
          [/[^\\"]+/, "string"],
          [/@escapes/, "string.escape"],
          [/\\./, "string.escape.invalid"],
          [/"/, { token: "string.quote", bracket: "@close", next: "@pop" }]
        ]
      }
    });
  }

  // The full set of custom languages this page contributes, keyed by id.
  var languages = { glsl: registerGlsl };

  window.__tbxLanguages = {
    // Registers every custom language with Monaco, once. Safe to call more than once.
    register: function (monaco) {
      Object.keys(languages).forEach(function (id) {
        if (registered[id]) return;
        registered[id] = true;
        languages[id](monaco);
      });
    }
  };
})();
