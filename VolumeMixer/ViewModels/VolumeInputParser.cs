using System.Globalization;

namespace VolumeMixer.ViewModels;

/// <summary>Regra única de digitação (master e apps): inteiro 0–100.</summary>
public static class VolumeInputParser
{
    /// <returns>null se vazio/inválido (chamador reverte o texto); senão valor clampado.</returns>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return null;
        return Math.Clamp(value, 0, 100);
    }
}
