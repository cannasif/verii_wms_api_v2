namespace verii_wms_api_v2.Modules.Quality.Domain;

/// <summary>
/// Single source of truth for the minimum physical GKK sample quantity.
/// The result is capped by the lot quantity and percentage results are rounded up so that
/// a fractional sample can never weaken the configured minimum.
/// A stock/stock-group rule is required for a floor; FixedQuantity 0 means no minimum.
/// </summary>
public static class QualitySamplingCalculator
{
    public static decimal Calculate(decimal lotQuantity, QualitySamplingMode mode, decimal value)
    {
        if (lotQuantity <= 0) return 0;

        return mode switch
        {
            QualitySamplingMode.Percentage => Math.Min(
                lotQuantity,
                Math.Ceiling(lotQuantity * Math.Clamp(value, 0, 100) / 100m)),
            QualitySamplingMode.FixedQuantity => Math.Min(lotQuantity, Math.Max(0, value)),
            // EveryNthHandlingUnit requires explicit handling-unit data. Until that dimension is
            // available, checking the complete lot is the safe, non-under-sampling behaviour.
            QualitySamplingMode.EveryNthHandlingUnit => lotQuantity,
            _ => lotQuantity
        };
    }
}
