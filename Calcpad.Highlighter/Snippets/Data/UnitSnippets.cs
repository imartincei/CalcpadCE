using Calcpad.Highlighter.Snippets.Models;

namespace Calcpad.Highlighter.Snippets.Data
{
    /// <summary>
    /// Snippet definitions for units (metric and imperial).
    /// </summary>
    public static class UnitSnippets
    {
        public static readonly SnippetItem[] Items =
        [
            // ============================================
            // DIMENSIONLESS
            // ============================================
            new SnippetItem { Insert = "%", Description = "Percent", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "‰", Description = "Per mille (per thousand)", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "‱", Description = "Per myriad (per ten thousand)", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "pcm", Description = "Per 100,000", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "ppm", Description = "Parts per million", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "ppb", Description = "Parts per billion", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "ppt", Description = "Parts per trillion", Category = "Units/Dimensionless", KeywordType = "Unit" },
            new SnippetItem { Insert = "ppq", Description = "Parts per quadrillion", Category = "Units/Dimensionless", KeywordType = "Unit" },

            // ============================================
            // ANGLES
            // ============================================
            new SnippetItem { Insert = "°", Description = "Degrees", Category = "Units/Angles", KeywordType = "Unit" },
            new SnippetItem { Insert = "′", Description = "Arc minutes", Category = "Units/Angles", KeywordType = "Unit" },
            new SnippetItem { Insert = "″", Description = "Arc seconds", Category = "Units/Angles", KeywordType = "Unit" },
            new SnippetItem { Insert = "deg", Description = "Degrees", Category = "Units/Angles", KeywordType = "Unit" },
            new SnippetItem { Insert = "rad", Description = "Radians", Category = "Units/Angles", KeywordType = "Unit" },
            new SnippetItem { Insert = "grad", Description = "Gradians", Category = "Units/Angles", KeywordType = "Unit" },
            new SnippetItem { Insert = "rev", Description = "Revolutions", Category = "Units/Angles", KeywordType = "Unit" },

            // ============================================
            // METRIC - MASS
            // ============================================
            new SnippetItem { Insert = "g", Description = "Gram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "dag", Description = "Decagram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "hg", Description = "Hectogram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "kg", Description = "Kilogram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "t", Description = "Metric ton", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "kt", Description = "Kiloton", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "Mt", Description = "Megaton", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "Gt", Description = "Gigaton", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "dg", Description = "Decigram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "cg", Description = "Centigram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "mg", Description = "Milligram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "μg", Description = "Microgram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "ng", Description = "Nanogram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "pg", Description = "Picogram", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "Da", Description = "Dalton (atomic mass unit)", Category = "Units/Metric/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "u", Description = "Unified atomic mass unit", Category = "Units/Metric/Mass", KeywordType = "Unit" },

            // ============================================
            // METRIC - LENGTH
            // ============================================
            new SnippetItem { Insert = "m", Description = "Meter", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "km", Description = "Kilometer", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "dm", Description = "Decimeter", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "cm", Description = "Centimeter", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "mm", Description = "Millimeter", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "μm", Description = "Micrometer", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "nm", Description = "Nanometer", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "pm", Description = "Picometer", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "AU", Description = "Astronomical unit", Category = "Units/Metric/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "ly", Description = "Light year", Category = "Units/Metric/Length", KeywordType = "Unit" },

            // ============================================
            // METRIC - AREA
            // ============================================
            new SnippetItem { Insert = "a", Description = "Are (100 m²)", Category = "Units/Metric/Area", KeywordType = "Unit" },
            new SnippetItem { Insert = "daa", Description = "Decare", Category = "Units/Metric/Area", KeywordType = "Unit" },
            new SnippetItem { Insert = "ha", Description = "Hectare", Category = "Units/Metric/Area", KeywordType = "Unit" },

            // ============================================
            // METRIC - VOLUME
            // ============================================
            new SnippetItem { Insert = "L", Description = "Liter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "daL", Description = "Decaliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "hL", Description = "Hectoliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "dL", Description = "Deciliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "cL", Description = "Centiliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "mL", Description = "Milliliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "μL", Description = "Microliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "nL", Description = "Nanoliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pL", Description = "Picoliter", Category = "Units/Metric/Volume", KeywordType = "Unit" },

            // ============================================
            // TIME
            // ============================================
            new SnippetItem { Insert = "s", Description = "Second", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "ms", Description = "Millisecond", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "μs", Description = "Microsecond", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "ns", Description = "Nanosecond", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "ps", Description = "Picosecond", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "min", Description = "Minute", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "h", Description = "Hour", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "d", Description = "Day", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "w", Description = "Week", Category = "Units/Time", KeywordType = "Unit" },
            new SnippetItem { Insert = "y", Description = "Year", Category = "Units/Time", KeywordType = "Unit" },

            // ============================================
            // FREQUENCY
            // ============================================
            new SnippetItem { Insert = "Hz", Description = "Hertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "kHz", Description = "Kilohertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "MHz", Description = "Megahertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "GHz", Description = "Gigahertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "THz", Description = "Terahertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "mHz", Description = "Millihertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "μHz", Description = "Microhertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "nHz", Description = "Nanohertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "pHz", Description = "Picohertz", Category = "Units/Frequency", KeywordType = "Unit" },
            new SnippetItem { Insert = "rpm", Description = "Revolutions per minute", Category = "Units/Frequency", KeywordType = "Unit" },

            // ============================================
            // SPEED
            // ============================================
            new SnippetItem { Insert = "kmh", Description = "Kilometers per hour", Category = "Units/Speed", KeywordType = "Unit" },

            // ============================================
            // TEMPERATURE
            // ============================================
            new SnippetItem { Insert = "°C", Description = "Degrees Celsius", Category = "Units/Temperature", KeywordType = "Unit" },
            new SnippetItem { Insert = "Δ°C", Description = "Temperature difference in Celsius", Category = "Units/Temperature", KeywordType = "Unit" },
            new SnippetItem { Insert = "K", Description = "Kelvin", Category = "Units/Temperature", KeywordType = "Unit" },

            // ============================================
            // FORCE
            // ============================================
            new SnippetItem { Insert = "N", Description = "Newton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "daN", Description = "Decanewton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "hN", Description = "Hectonewton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "kN", Description = "Kilonewton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "MN", Description = "Meganewton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "GN", Description = "Giganewton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "TN", Description = "Teranewton", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "gf", Description = "Gram-force", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "kgf", Description = "Kilogram-force", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "tf", Description = "Tonne-force", Category = "Units/Metric/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "dyn", Description = "Dyne", Category = "Units/Metric/Force", KeywordType = "Unit" },

            // ============================================
            // MOMENT/TORQUE
            // ============================================
            new SnippetItem { Insert = "Nm", Description = "Newton-meter", Category = "Units/Metric/Moment", KeywordType = "Unit" },
            new SnippetItem { Insert = "kNm", Description = "Kilonewton-meter", Category = "Units/Metric/Moment", KeywordType = "Unit" },

            // ============================================
            // PRESSURE
            // ============================================
            new SnippetItem { Insert = "Pa", Description = "Pascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "daPa", Description = "Decapascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "hPa", Description = "Hectopascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "kPa", Description = "Kilopascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "MPa", Description = "Megapascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "GPa", Description = "Gigapascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "TPa", Description = "Terapascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "dPa", Description = "Decipascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "cPa", Description = "Centipascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "mPa", Description = "Millipascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "μPa", Description = "Micropascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "nPa", Description = "Nanopascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "pPa", Description = "Picopascal", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "bar", Description = "Bar", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "mbar", Description = "Millibar", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "μbar", Description = "Microbar", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "atm", Description = "Standard atmosphere", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "at", Description = "Technical atmosphere", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "Torr", Description = "Torr", Category = "Units/Metric/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "mmHg", Description = "Millimeters of mercury", Category = "Units/Metric/Pressure", KeywordType = "Unit" },

            // ============================================
            // VISCOSITY
            // ============================================
            new SnippetItem { Insert = "P", Description = "Poise", Category = "Units/Metric/Viscosity", KeywordType = "Unit" },
            new SnippetItem { Insert = "cP", Description = "Centipoise", Category = "Units/Metric/Viscosity", KeywordType = "Unit" },
            new SnippetItem { Insert = "St", Description = "Stokes", Category = "Units/Metric/Viscosity", KeywordType = "Unit" },
            new SnippetItem { Insert = "cSt", Description = "Centistokes", Category = "Units/Metric/Viscosity", KeywordType = "Unit" },

            // ============================================
            // ENERGY/WORK
            // ============================================
            new SnippetItem { Insert = "J", Description = "Joule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "kJ", Description = "Kilojoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "MJ", Description = "Megajoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "GJ", Description = "Gigajoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "TJ", Description = "Terajoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "mJ", Description = "Millijoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "μJ", Description = "Microjoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "nJ", Description = "Nanojoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "pJ", Description = "Picojoule", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "Wh", Description = "Watt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "kWh", Description = "Kilowatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "MWh", Description = "Megawatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "GWh", Description = "Gigawatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "TWh", Description = "Terawatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "mWh", Description = "Milliwatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "μWh", Description = "Microwatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "nWh", Description = "Nanowatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "pWh", Description = "Picowatt-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "VAh", Description = "Volt-ampere-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "kVAh", Description = "Kilovolt-ampere-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "MVAh", Description = "Megavolt-ampere-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "GVAh", Description = "Gigavolt-ampere-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "VARh", Description = "Volt-ampere-reactive-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "kVARh", Description = "Kilovolt-ampere-reactive-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "MVARh", Description = "Megavolt-ampere-reactive-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "GVARh", Description = "Gigavolt-ampere-reactive-hour", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "eV", Description = "Electronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "keV", Description = "Kiloelectronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "MeV", Description = "Megaelectronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "GeV", Description = "Gigaelectronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "TeV", Description = "Teraelectronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "PeV", Description = "Petaelectronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "EeV", Description = "Exaelectronvolt", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "cal", Description = "Calorie", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "kcal", Description = "Kilocalorie", Category = "Units/Metric/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "erg", Description = "Erg", Category = "Units/Metric/Energy", KeywordType = "Unit" },

            // ============================================
            // POWER
            // ============================================
            new SnippetItem { Insert = "W", Description = "Watt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "kW", Description = "Kilowatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "MW", Description = "Megawatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "GW", Description = "Gigawatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "TW", Description = "Terawatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "mW", Description = "Milliwatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "μW", Description = "Microwatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "nW", Description = "Nanowatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "pW", Description = "Picowatt", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "VA", Description = "Volt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "kVA", Description = "Kilovolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "MVA", Description = "Megavolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "GVA", Description = "Gigavolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "TVA", Description = "Teravolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "mVA", Description = "Millivolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "μVA", Description = "Microvolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "nVA", Description = "Nanovolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "pVA", Description = "Picovolt-ampere", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "VAR", Description = "Volt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "kVAR", Description = "Kilovolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "MVAR", Description = "Megavolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "GVAR", Description = "Gigavolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "TVAR", Description = "Teravolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "mVAR", Description = "Millivolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "μVAR", Description = "Microvolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "nVAR", Description = "Nanovolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "pVAR", Description = "Picovolt-ampere reactive", Category = "Units/Metric/Power", KeywordType = "Unit" },

            // ============================================
            // ELECTRIC CURRENT
            // ============================================
            new SnippetItem { Insert = "A", Description = "Ampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "kA", Description = "Kiloampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "MA", Description = "Megaampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "GA", Description = "Gigaampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "TA", Description = "Teraampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "mA", Description = "Milliampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "μA", Description = "Microampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "nA", Description = "Nanoampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },
            new SnippetItem { Insert = "pA", Description = "Picoampere", Category = "Units/Electrical/Current", KeywordType = "Unit" },

            // ============================================
            // ELECTRIC CHARGE
            // ============================================
            new SnippetItem { Insert = "C", Description = "Coulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "kC", Description = "Kilocoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "MC", Description = "Megacoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "GC", Description = "Gigacoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "TC", Description = "Teracoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "mC", Description = "Millicoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "μC", Description = "Microcoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "nC", Description = "Nanocoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "pC", Description = "Picocoulomb", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "Ah", Description = "Ampere-hour", Category = "Units/Electrical/Charge", KeywordType = "Unit" },
            new SnippetItem { Insert = "mAh", Description = "Milliampere-hour", Category = "Units/Electrical/Charge", KeywordType = "Unit" },

            // ============================================
            // ELECTRIC POTENTIAL
            // ============================================
            new SnippetItem { Insert = "V", Description = "Volt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "kV", Description = "Kilovolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "MV", Description = "Megavolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "GV", Description = "Gigavolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "TV", Description = "Teravolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "mV", Description = "Millivolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "μV", Description = "Microvolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "nV", Description = "Nanovolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },
            new SnippetItem { Insert = "pV", Description = "Picovolt", Category = "Units/Electrical/Potential", KeywordType = "Unit" },

            // ============================================
            // CAPACITANCE
            // ============================================
            new SnippetItem { Insert = "F", Description = "Farad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "kF", Description = "Kilofarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "MF", Description = "Megafarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "GF", Description = "Gigafarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "TF", Description = "Terafarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "mF", Description = "Millifarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "μF", Description = "Microfarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "nF", Description = "Nanofarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },
            new SnippetItem { Insert = "pF", Description = "Picofarad", Category = "Units/Electrical/Capacitance", KeywordType = "Unit" },

            // ============================================
            // RESISTANCE
            // ============================================
            new SnippetItem { Insert = "Ω", Description = "Ohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "kΩ", Description = "Kilohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "MΩ", Description = "Megohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "GΩ", Description = "Gigohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "TΩ", Description = "Teraohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "mΩ", Description = "Milliohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "μΩ", Description = "Microhm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "nΩ", Description = "Nanoohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },
            new SnippetItem { Insert = "pΩ", Description = "Picoohm", Category = "Units/Electrical/Resistance", KeywordType = "Unit" },

            // ============================================
            // CONDUCTANCE
            // ============================================
            new SnippetItem { Insert = "S", Description = "Siemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "kS", Description = "Kilosiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "MS", Description = "Megasiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "GS", Description = "Gigasiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "TS", Description = "Terasiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "mS", Description = "Millisiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "μS", Description = "Microsiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "nS", Description = "Nanosiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "pS", Description = "Picosiemens", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "℧", Description = "Mho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "k℧", Description = "Kilomho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "M℧", Description = "Megamho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "G℧", Description = "Gigamho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "T℧", Description = "Teramho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "m℧", Description = "Millimho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "μ℧", Description = "Micromho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "n℧", Description = "Nanomho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "p℧", Description = "Picomho", Category = "Units/Electrical/Conductance", KeywordType = "Unit" },

            // ============================================
            // MAGNETIC FLUX
            // ============================================
            new SnippetItem { Insert = "Wb", Description = "Weber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "kWb", Description = "Kiloweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "MWb", Description = "Megaweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "GWb", Description = "Gigaweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "TWb", Description = "Teraweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "mWb", Description = "Milliweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "μWb", Description = "Microweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "nWb", Description = "Nanoweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },
            new SnippetItem { Insert = "pWb", Description = "Picoweber", Category = "Units/Electrical/Magnetic Flux", KeywordType = "Unit" },

            // ============================================
            // MAGNETIC FLUX DENSITY
            // ============================================
            new SnippetItem { Insert = "T", Description = "Tesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "kT", Description = "Kilotesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "MT", Description = "Megatesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "GT", Description = "Gigatesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "TT", Description = "Teratesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "mT", Description = "Millitesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "μT", Description = "Microtesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "nT", Description = "Nanotesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },
            new SnippetItem { Insert = "pT", Description = "Picotesla", Category = "Units/Electrical/Magnetic Flux Density", KeywordType = "Unit" },

            // ============================================
            // INDUCTANCE
            // ============================================
            new SnippetItem { Insert = "H", Description = "Henry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "kH", Description = "Kilohenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "MH", Description = "Megahenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "GH", Description = "Gigahenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "TH", Description = "Terahenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "mH", Description = "Millihenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "μH", Description = "Microhenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "nH", Description = "Nanohenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },
            new SnippetItem { Insert = "pH", Description = "Picohenry", Category = "Units/Electrical/Inductance", KeywordType = "Unit" },

            // ============================================
            // LUMINOUS
            // ============================================
            new SnippetItem { Insert = "cd", Description = "Candela", Category = "Units/Luminous", KeywordType = "Unit" },
            new SnippetItem { Insert = "lm", Description = "Lumen", Category = "Units/Luminous", KeywordType = "Unit" },
            new SnippetItem { Insert = "lx", Description = "Lux", Category = "Units/Luminous", KeywordType = "Unit" },

            // ============================================
            // RADIOACTIVITY
            // ============================================
            new SnippetItem { Insert = "Bq", Description = "Becquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "kBq", Description = "Kilobecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "MBq", Description = "Megabecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "GBq", Description = "Gigabecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "TBq", Description = "Terabecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "mBq", Description = "Millibecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "μBq", Description = "Microbecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "nBq", Description = "Nanobecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "pBq", Description = "Picobecquerel", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "Ci", Description = "Curie", Category = "Units/Radioactivity", KeywordType = "Unit" },
            new SnippetItem { Insert = "Rd", Description = "Rutherford", Category = "Units/Radioactivity", KeywordType = "Unit" },

            // ============================================
            // ABSORBED DOSE
            // ============================================
            new SnippetItem { Insert = "Gy", Description = "Gray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "kGy", Description = "Kilogray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "MGy", Description = "Megagray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "GGy", Description = "Gigagray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "TGy", Description = "Teragray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "mGy", Description = "Milligray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "μGy", Description = "Microgray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "nGy", Description = "Nanogray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },
            new SnippetItem { Insert = "pGy", Description = "Picogray", Category = "Units/Dose/Absorbed", KeywordType = "Unit" },

            // ============================================
            // EQUIVALENT DOSE
            // ============================================
            new SnippetItem { Insert = "Sv", Description = "Sievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "kSv", Description = "Kilosievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "MSv", Description = "Megasievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "GSv", Description = "Gigasievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "TSv", Description = "Terasievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "mSv", Description = "Millisievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "μSv", Description = "Microsievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "nSv", Description = "Nanosievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },
            new SnippetItem { Insert = "pSv", Description = "Picosievert", Category = "Units/Dose/Equivalent", KeywordType = "Unit" },

            // ============================================
            // CATALYTIC ACTIVITY
            // ============================================
            new SnippetItem { Insert = "kat", Description = "Katal", Category = "Units/Catalytic Activity", KeywordType = "Unit" },

            // ============================================
            // AMOUNT OF SUBSTANCE
            // ============================================
            new SnippetItem { Insert = "mol", Description = "Mole", Category = "Units/Amount of Substance", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - MASS
            // ============================================
            new SnippetItem { Insert = "gr", Description = "Grain", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "dr", Description = "Dram", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "oz", Description = "Ounce", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "lb", Description = "Pound", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "lbm", Description = "Pound-mass", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "lb_m", Description = "Pound-mass", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "kipm", Description = "Kip-mass (1000 lbm)", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "kip_m", Description = "Kip-mass (1000 lbm)", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "klb", Description = "Kilopound (1000 lbm)", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "st", Description = "Stone", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "qr", Description = "Quarter", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "cwt", Description = "Hundredweight", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "cwt_US", Description = "US hundredweight", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "cwt_UK", Description = "UK hundredweight", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "ton", Description = "Short/Long ton", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "ton_US", Description = "US short ton", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "ton_UK", Description = "UK long ton", Category = "Units/Imperial/Mass", KeywordType = "Unit" },
            new SnippetItem { Insert = "slug", Description = "Slug", Category = "Units/Imperial/Mass", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - LENGTH
            // ============================================
            new SnippetItem { Insert = "th", Description = "Thou (mil)", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "in", Description = "Inch", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "ft", Description = "Foot", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "yd", Description = "Yard", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "ch", Description = "Chain", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "fur", Description = "Furlong", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "mi", Description = "Mile", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "ftm", Description = "Fathom", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "ftm_UK", Description = "UK fathom", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "ftm_US", Description = "US fathom", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "cable", Description = "Cable length", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "cable_UK", Description = "UK cable length", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "cable_US", Description = "US cable length", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "nmi", Description = "Nautical mile", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "li", Description = "Link", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "rod", Description = "Rod", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "pole", Description = "Pole", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "perch", Description = "Perch", Category = "Units/Imperial/Length", KeywordType = "Unit" },
            new SnippetItem { Insert = "lea", Description = "League", Category = "Units/Imperial/Length", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - SPEED
            // ============================================
            new SnippetItem { Insert = "mph", Description = "Miles per hour", Category = "Units/Imperial/Speed", KeywordType = "Unit" },
            new SnippetItem { Insert = "knot", Description = "Knot", Category = "Units/Imperial/Speed", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - TEMPERATURE
            // ============================================
            new SnippetItem { Insert = "°F", Description = "Degrees Fahrenheit", Category = "Units/Imperial/Temperature", KeywordType = "Unit" },
            new SnippetItem { Insert = "Δ°F", Description = "Temperature difference in Fahrenheit", Category = "Units/Imperial/Temperature", KeywordType = "Unit" },
            new SnippetItem { Insert = "°R", Description = "Degrees Rankine", Category = "Units/Imperial/Temperature", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - AREA
            // ============================================
            new SnippetItem { Insert = "rood", Description = "Rood", Category = "Units/Imperial/Area", KeywordType = "Unit" },
            new SnippetItem { Insert = "ac", Description = "Acre", Category = "Units/Imperial/Area", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - VOLUME (FLUID)
            // ============================================
            new SnippetItem { Insert = "fl_dr_UK", Description = "UK fluid dram", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "fl_dr_US", Description = "US fluid dram", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "fl_oz", Description = "Fluid ounce", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "fl_oz_UK", Description = "UK fluid ounce", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "fl_oz_US", Description = "US fluid ounce", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gi", Description = "Gill", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gi_UK", Description = "UK gill", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gi_US", Description = "US gill", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pt", Description = "Pint", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pt_UK", Description = "UK pint", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pt_US", Description = "US pint", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pt_dry", Description = "US dry pint", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "qt", Description = "Quart", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "qt_UK", Description = "UK quart", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "qt_US", Description = "US quart", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "qt_dry", Description = "US dry quart", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gal", Description = "Gallon", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gal_UK", Description = "UK gallon", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gal_US", Description = "US gallon", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "gal_dry", Description = "US dry gallon", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "bbl", Description = "Barrel", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "bbl_UK", Description = "UK barrel", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "bbl_US", Description = "US barrel (oil)", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "bbl_dry", Description = "US dry barrel", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pk_UK", Description = "UK peck", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "pk_US", Description = "US dry peck", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "bu_UK", Description = "UK bushel", Category = "Units/Imperial/Volume", KeywordType = "Unit" },
            new SnippetItem { Insert = "bu_US", Description = "US dry bushel", Category = "Units/Imperial/Volume", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - FORCE
            // ============================================
            new SnippetItem { Insert = "ozf", Description = "Ounce-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "oz_f", Description = "Ounce-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "lbf", Description = "Pound-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "lb_f", Description = "Pound-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "kip", Description = "Kip (1000 lbf)", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "kipf", Description = "Kip-force (1000 lbf)", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "kip_f", Description = "Kip-force (1000 lbf)", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "tonf", Description = "Ton-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "tonf_US", Description = "US short ton-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "tonf_UK", Description = "UK long ton-force", Category = "Units/Imperial/Force", KeywordType = "Unit" },
            new SnippetItem { Insert = "pdl", Description = "Poundal", Category = "Units/Imperial/Force", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - PRESSURE
            // ============================================
            new SnippetItem { Insert = "osi", Description = "Ounces per square inch", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "osf", Description = "Ounces per square foot", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "psi", Description = "Pounds per square inch", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "psf", Description = "Pounds per square foot", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "ksi", Description = "Kips per square inch", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "ksf", Description = "Kips per square foot", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "tsi", Description = "Tons per square inch", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "tsf", Description = "Tons per square foot", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },
            new SnippetItem { Insert = "inHg", Description = "Inches of mercury", Category = "Units/Imperial/Pressure", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - ENERGY
            // ============================================
            new SnippetItem { Insert = "BTU", Description = "British thermal unit", Category = "Units/Imperial/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "therm", Description = "Therm", Category = "Units/Imperial/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "therm_US", Description = "US therm", Category = "Units/Imperial/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "therm_UK", Description = "UK therm", Category = "Units/Imperial/Energy", KeywordType = "Unit" },
            new SnippetItem { Insert = "quad", Description = "Quad", Category = "Units/Imperial/Energy", KeywordType = "Unit" },

            // ============================================
            // IMPERIAL/US - POWER
            // ============================================
            new SnippetItem { Insert = "hp", Description = "Mechanical horsepower", Category = "Units/Imperial/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "hp_E", Description = "Electrical horsepower", Category = "Units/Imperial/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "hp_S", Description = "Boiler horsepower", Category = "Units/Imperial/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "hp_M", Description = "Metric horsepower", Category = "Units/Imperial/Power", KeywordType = "Unit" },
            new SnippetItem { Insert = "ks", Description = "Metric horsepower (konjska snaga)", Category = "Units/Imperial/Power", KeywordType = "Unit" }
        ];
    }
}