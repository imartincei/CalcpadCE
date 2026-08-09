using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Calcpad.Core
{
    /// <summary>A value the directive supplied that its own rules reject.</summary>
    public readonly record struct DirectiveError<TKey>(TKey Key, string Message) where TKey : struct, Enum;

    public abstract class DirectiveDto<TSelf, TKey>
        where TSelf : DirectiveDto<TSelf, TKey>
        where TKey : struct, Enum
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Recognized keys, derived from <typeparamref name="TKey"/> (case-insensitive).</summary>
        public static readonly IReadOnlySet<string> KnownKeys =
            new HashSet<string>(Enum.GetNames<TKey>(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Deserializes the payload. Throws <see cref="JsonException"/> on malformed JSON or a wrong value type.</summary>
        public static TSelf Parse(string json) => JsonSerializer.Deserialize<TSelf>(json, Options);

        /// <summary>An entry for every supplied value that the directive rejects.</summary>
        public IReadOnlyList<DirectiveError<TKey>> Validate()
        {
            var errors = new List<DirectiveError<TKey>>();
            Validate(errors);
            return errors;
        }

        protected abstract void Validate(List<DirectiveError<TKey>> errors);

        protected static void CheckRange<T>(List<DirectiveError<TKey>> errors, TKey key, string name, T? value, double min, double? max) where T : struct, IConvertible
        {
            if (value is null)
                return;

            var d = value.Value.ToDouble(null);
            if (d < min || max.HasValue && d > max.Value)
                errors.Add(new(key, max.HasValue
                    ? $"'{name}' must be between {min} and {max.Value}"
                    : $"'{name}' must be at least {min}"));
        }
    }
}
