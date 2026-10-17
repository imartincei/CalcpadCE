#nullable enable
namespace Calcpad.Core;

/// <summary>A machine-readable scalar result captured from a worksheet assignment.</summary>
public sealed record ScalarResult(double Real, double Imaginary, bool IsComplex, string? Unit);
