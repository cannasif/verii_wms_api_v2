using verii_wms_api_v2.Modules.SteelReceipt.Domain;

namespace verii_wms_api_v2.Modules.SteelReceipt.Application;

internal static class SteelReceiptPlanStatusRules
{
    internal static SteelReceiptPlanStatus Resolve(IEnumerable<LineState> lines)
    {
        var states = lines as IReadOnlyList<LineState> ?? lines.ToList();
        return ResolveFromAggregates(
            states.Count > 0,
            states.Count > 0 && states.All(x => x.ConversionStatus == SteelReceiptConversionStatus.Created),
            states.Any(x => x.ConversionStatus == SteelReceiptConversionStatus.Created),
            states.Any(x => x.InspectionStatus is SteelInspectionStatus.Approved or SteelInspectionStatus.PartiallyApproved),
            states.Any(x => x.InspectionStatus == SteelInspectionStatus.Pending),
            states.Any(x => x.InspectionStatus != SteelInspectionStatus.Pending));
    }

    internal static SteelReceiptPlanStatus ResolveFromAggregates(
        bool hasLines,
        bool allConverted,
        bool anyConverted,
        bool hasApproved,
        bool hasPending,
        bool anyNonPendingInspection)
    {
        if (!hasLines)
            return SteelReceiptPlanStatus.Imported;
        if (allConverted)
            return SteelReceiptPlanStatus.Converted;
        if (anyConverted)
            return SteelReceiptPlanStatus.PartiallyConverted;
        if (hasApproved && hasPending)
            return SteelReceiptPlanStatus.PartiallyReadyForReceipt;
        if (hasApproved)
            return SteelReceiptPlanStatus.ReadyForReceipt;
        if (anyNonPendingInspection)
            return SteelReceiptPlanStatus.InspectionInProgress;

        return SteelReceiptPlanStatus.Imported;
    }

    internal static SteelReceiptPlanStatus SelectGridStatus(
        SteelReceiptPlanStatus persistedStatus,
        bool hasLines,
        bool allConverted,
        bool anyConverted,
        bool hasApproved,
        bool hasPending,
        bool anyNonPendingInspection) =>
        persistedStatus == SteelReceiptPlanStatus.Cancelled
            ? SteelReceiptPlanStatus.Cancelled
            : ResolveFromAggregates(
                hasLines,
                allConverted,
                anyConverted,
                hasApproved,
                hasPending,
                anyNonPendingInspection);

    internal readonly record struct LineState(SteelInspectionStatus InspectionStatus, SteelReceiptConversionStatus ConversionStatus);
}
