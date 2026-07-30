using verii_wms_api_v2.Modules.SteelReceipt.Domain;

namespace verii_wms_api_v2.Modules.SteelReceipt.Application;

internal static class SteelReceiptPlanStatusRules
{
    internal static SteelReceiptPlanStatus Resolve(IEnumerable<LineState> lines)
    {
        var states = lines as IReadOnlyList<LineState> ?? lines.ToList();
        if (states.Count == 0)
            return SteelReceiptPlanStatus.Imported;

        if (states.All(x => x.ConversionStatus == SteelReceiptConversionStatus.Created))
            return SteelReceiptPlanStatus.Converted;
        if (states.Any(x => x.ConversionStatus == SteelReceiptConversionStatus.Created))
            return SteelReceiptPlanStatus.PartiallyConverted;

        var hasApproved = states.Any(x => x.InspectionStatus is SteelInspectionStatus.Approved or SteelInspectionStatus.PartiallyApproved);
        var hasPending = states.Any(x => x.InspectionStatus == SteelInspectionStatus.Pending);
        if (hasApproved && hasPending)
            return SteelReceiptPlanStatus.PartiallyReadyForReceipt;
        if (hasApproved)
            return SteelReceiptPlanStatus.ReadyForReceipt;
        if (states.Any(x => x.InspectionStatus != SteelInspectionStatus.Pending))
            return SteelReceiptPlanStatus.InspectionInProgress;

        return SteelReceiptPlanStatus.Imported;
    }

    internal readonly record struct LineState(SteelInspectionStatus InspectionStatus, SteelReceiptConversionStatus ConversionStatus);
}
