#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Calcpad.Highlighter.Linter.Models;
using Calcpad.Highlighter.Snippets;
using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Linter.Constants
{
    /// <summary>
    /// Registry of built-in function signatures derived from SnippetRegistry.
    /// Provides access to function parameter types and return types for linting.
    /// </summary>
    public static class FunctionSignatures
    {
        private static FrozenDictionary<string, FunctionSignature>? _signatures;
        private static FrozenDictionary<string, FunctionSignature[]>? _allOverloads;

        /// <summary>
        /// Gets the function signatures dictionary (lazily built from SnippetRegistry).
        /// For functions with multiple overloads, returns the most permissive signature.
        /// </summary>
        public static FrozenDictionary<string, FunctionSignature> Signatures
        {
            get
            {
                if (_signatures != null) return _signatures;
                _signatures = BuildSignaturesFromSnippets();
                return _signatures;
            }
        }

        /// <summary>
        /// Gets all function overloads (lazily built from SnippetRegistry).
        /// For functions like take, line, spline that have multiple valid signatures.
        /// </summary>
        public static FrozenDictionary<string, FunctionSignature[]> AllOverloads
        {
            get
            {
                if (_allOverloads != null) return _allOverloads;
                _allOverloads = BuildAllOverloadsFromSnippets();
                return _allOverloads;
            }
        }

        /// <summary>
        /// Builds the signatures dictionary from the snippet data.
        /// </summary>
        private static FrozenDictionary<string, FunctionSignature> BuildSignaturesFromSnippets()
        {
            var snippets = SnippetRegistry.GetFunctionSnippetsByName();
            var signatures = new Dictionary<string, FunctionSignature>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in snippets)
            {
                var snippet = kvp.Value;
                signatures[kvp.Key] = SnippetToSignature(kvp.Key, snippet);
            }

            return signatures.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the all overloads dictionary from the snippet data.
        /// </summary>
        private static FrozenDictionary<string, FunctionSignature[]> BuildAllOverloadsFromSnippets()
        {
            var overloads = SnippetRegistry.GetFunctionOverloads();
            var signatures = new Dictionary<string, FunctionSignature[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in overloads)
            {
                signatures[kvp.Key] = kvp.Value.Select(s => SnippetToSignature(kvp.Key, s)).ToArray();
            }

            return signatures.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts a SnippetItem to a FunctionSignature.
        /// </summary>
        private static FunctionSignature SnippetToSignature(string name, SnippetItem snippet)
        {
            return new FunctionSignature
            {
                Name = name,
                MinParams = snippet.MinParams,
                MaxParams = snippet.MaxParams,
                ParameterTypes = snippet.Parameters?.Select(p => p.Type).ToArray() ?? Array.Empty<ParameterType>(),
                ReturnType = snippet.ReturnType ?? CalcpadType.Value,
                Description = snippet.Description,
                IsElementWise = snippet.IsElementWise,
                AcceptsAnyCount = snippet.AcceptsAnyCount
            };
        }

        /// <summary>
        /// Gets the signature for a function by name.
        /// For functions with multiple overloads, returns the most permissive signature.
        /// </summary>
        public static FunctionSignature? GetSignature(string functionName)
        {
            Signatures.TryGetValue(functionName, out var signature);
            return signature;
        }

        /// <summary>
        /// Gets all overloads for a function by name.
        /// Returns all valid signatures for functions like take, line, spline.
        /// </summary>
        public static FunctionSignature[] GetAllOverloads(string functionName)
        {
            AllOverloads.TryGetValue(functionName, out var overloads);
            return overloads ?? [];
        }

        /// <summary>
        /// Checks if a function has a defined signature.
        /// </summary>
        public static bool HasSignature(string functionName)
        {
            return Signatures.ContainsKey(functionName);
        }
    }
}
