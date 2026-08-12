using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for mathematical and physical constants.
    /// </summary>
    public static class ConstantSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // Mathematical Constants
            new SnippetItem
            {
                Insert = "π",
                Description = "Pi - ratio of circumference to diameter",
                Label = "  π",
                Category = "Constants",
                KeywordType = "Constant"
            },
            new SnippetItem
            {
                Insert = "pi",
                Description = "Pi - ratio of circumference to diameter (spelled out)",
                Label = " pi",
                Category = "Constants",
                KeywordType = "Constant"
            },
            new SnippetItem
            {
                Insert = "e",
                Description = "Euler's number - base of natural logarithm",
                Label = "  e",
                Category = "Constants",
                KeywordType = "Constant"
            },
            new SnippetItem
            {
                Insert = "φ = 1.618033988749894",
                Description = "Golden ratio",
                Label = "  φ",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "γ = 0.5772156649015328",
                Description = "Euler-Mascheroni constant",
                Label = "  γ",
                Category = "Constants"
            },

            // System Variables (defined by the engine, always available).
            // InternalOnly: kept here so the linter treats it as defined, but not
            // offered in the insert menu or completion.
            new SnippetItem
            {
                Insert = "Units",
                Description = "Drawing scale factor: output length-units per metre, taken from the worksheet's Units setting. m → 1, cm → 100, mm → 1000 (0 when no drawing units are set). Used to convert physical coordinates for $Plot/SVG output.",
                Label = "Units",
                Category = "System Variables",
                KeywordType = "Constant",
                InternalOnly = true
            },

            // Physical Constants - Mechanics
            new SnippetItem
            {
                Insert = "g = 9.80665m/s^2",
                Description = "Standard gravitational acceleration",
                Label = "  g",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "G = 6.67430*10^-11*(m^3/(kg*s^2))",
                Description = "Gravitational constant",
                Label = "  G",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "M_E = 5.9722*10^24*kg",
                Description = "Earth mass",
                Label = "ME",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "M_S = 1.98847*10^30*kg",
                Description = "Solar mass",
                Label = "MS",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "c = 299792458m/s",
                Description = "Speed of light in vacuum",
                Label = "  c",
                Category = "Constants"
            },

            // Physical Constants - Quantum/Electromagnetic
            new SnippetItem
            {
                Insert = "h = 6.62607015*10^-34*J/Hz",
                Description = "Planck constant",
                Label = "  h",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "μ_0 = 1.25663706212*10^-6*N/A^2",
                Description = "Vacuum permeability",
                Label = " μ0",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "ε_0 = 8.8541878128*10^-12*F/m",
                Description = "Vacuum permittivity",
                Label = " ε0",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "k_e = 8.9875517923*10^9*N*m^2/C^2",
                Description = "Coulomb constant",
                Label = " ke",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "e = 1.602176634*10^-19*C",
                Description = "Elementary charge",
                Label = "  e",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "m_e = 9.1093837015*10^-31*kg",
                Description = "Electron mass",
                Label = "me",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "m_p = 1.67262192369*10^-27*kg",
                Description = "Proton mass",
                Label = "mp",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "m_n = 1.67492749804*10^-27*kg",
                Description = "Neutron mass",
                Label = "mn",
                Category = "Constants"
            },

            // Physical Constants - Thermodynamics
            new SnippetItem
            {
                Insert = "N_A = 6.02214076*10^23*mol^-1",
                Description = "Avogadro constant",
                Label = "NA",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "σ = 5.670374419*10^-8*W*m^-2*K^-4",
                Description = "Stefan-Boltzmann constant",
                Label = "  σ",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "k_B = 1.380649*10^-23*J/K",
                Description = "Boltzmann constant",
                Label = " kB",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "R = 8.31446261815324*(J/(mol*K))",
                Description = "Molar gas constant",
                Label = "  R",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "F = 96485.33212331C/mol",
                Description = "Faraday constant",
                Label = "  F",
                Category = "Constants"
            },

            // Engineering Constants - Unit Weights
            new SnippetItem
            {
                Insert = "γ_c = 25kN/m^3",
                Description = "Unit weight of concrete",
                Label = " γc",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "γ_s = 78.5kN/m^3",
                Description = "Unit weight of steel",
                Label = " γs",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "γ_a = 27kN/m^3",
                Description = "Unit weight of aluminium",
                Label = " γa",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "γ_g = 25kN/m^3",
                Description = "Unit weight of glass",
                Label = " γg",
                Category = "Constants"
            },
            new SnippetItem
            {
                Insert = "γ_w = 10kN/m^3",
                Description = "Unit weight of water",
                Label = "γw",
                Category = "Constants"
            }
        ];
    }
}
