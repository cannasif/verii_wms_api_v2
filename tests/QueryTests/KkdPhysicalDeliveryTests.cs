using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

/// <summary>
/// KKD fiziksel teslim onayı: stok toplama sırasında bekleme rafına taşındığı için sevk belgesi
/// stoğu yeniden toplamaz ve teslim, tarayıcıdan değil tek sunucu işleminden yürür.
/// </summary>
public sealed class KkdPhysicalDeliveryTests
{
    private const long StagingLocationId = 77;

    [Fact]
    public void Serial_trackings_are_split_so_each_batch_carries_one_line_and_one_serial()
    {
        var lines = new[]
        {
            Line(stockId: 10, quantity: 2, Tracking(1, serialNo: "SER-1"), Tracking(1, serialNo: "SER-2")),
            Line(stockId: 20, quantity: 1, Tracking(1, serialNo: "SER-9"))
        };

        var batches = KkdPhysicalDeliveryService.BuildOperationBatches(lines, [101, 202], StagingLocationId);

        Assert.Equal(2, batches.Count);
        Assert.Equal([101, 202], batches[0].Select(x => x.LineId));
        Assert.Equal(["SER-1", "SER-9"], batches[0].Select(x => x.SerialNo));
        Assert.Equal([101], batches[1].Select(x => x.LineId));
        Assert.Equal(["SER-2"], batches[1].Select(x => x.SerialNo));
        Assert.All(batches.SelectMany(x => x), item => Assert.Equal(StagingLocationId, item.SourceLocationId));
    }

    [Fact]
    public void Lines_without_trackings_are_shipped_in_a_single_batch_with_the_full_quantity()
    {
        var lines = new[] { Line(stockId: 10, quantity: 3) };

        var batches = KkdPhysicalDeliveryService.BuildOperationBatches(lines, [101], StagingLocationId);

        var item = Assert.Single(Assert.Single(batches));
        Assert.Equal(3m, item.Quantity);
        Assert.Null(item.SerialNo);
        Assert.Equal(StagingLocationId, item.SourceLocationId);
    }

    [Fact]
    public void Staging_shelf_pick_is_recognised_as_a_no_op_movement()
    {
        var row = new StockMovementLineRequest(
            10, null, 1, SourceWarehouseId: 1, SourceLocationId: StagingLocationId,
            TargetWarehouseId: 1, TargetLocationId: StagingLocationId, "ADET", null, null, "Available");

        Assert.True(WarehouseOutboundOperationService.IsSameLocationTransfer(row));
    }

    [Fact]
    public void Shipment_out_of_the_staging_shelf_still_posts_a_movement()
    {
        var row = new StockMovementLineRequest(
            10, null, 1, SourceWarehouseId: 1, SourceLocationId: StagingLocationId,
            TargetWarehouseId: null, TargetLocationId: null, "ADET", null, null, "Available");

        Assert.False(WarehouseOutboundOperationService.IsSameLocationTransfer(row));
    }

    [Fact]
    public void Delivery_from_a_request_does_not_require_the_recipient_to_have_a_wms_user()
    {
        var employee = new KkdEmployee { EmployeeCode = "P-1", BranchCode = "0" };

        KkdDistributionService.ValidatePolicy(Request(kkdRequestId: 55), employee, Policy());
    }

    [Fact]
    public void Direct_distribution_still_requires_the_recipient_to_have_a_wms_user()
    {
        var employee = new KkdEmployee { EmployeeCode = "P-1", BranchCode = "0" };

        var exception = Assert.Throws<AppException>(() =>
            KkdDistributionService.ValidatePolicy(Request(kkdRequestId: null), employee, Policy()));

        Assert.Equal(409, exception.StatusCode);
    }

    private static KkdPhysicalDeliveryService.PreparedDeliveryLine Line(
        long stockId,
        decimal quantity,
        params KkdDistributionTrackingRequest[] trackings) =>
        new(
            new KkdDistributionLineCreateRequest(
                stockId, null, quantity, "ADET", StagingLocationId, null, null, false, null, trackings, 1),
            trackings);

    private static KkdDistributionTrackingRequest Tracking(decimal quantity, string? serialNo = null, string? lotNo = null) =>
        new(quantity, lotNo, serialNo, null, null, null, StagingLocationId);

    private static KkdDistributionCreateRequest Request(long? kkdRequestId) => new(
        Guid.NewGuid(), 1, 1, 1, DateOnly.FromDateTime(DateTime.UtcNow), StagingLocationId, null, null,
        [new KkdDistributionLineCreateRequest(10, null, 1, "ADET", StagingLocationId, null, null, false, null, null, 1)],
        KkdRequestId: kkdRequestId);

    private static KkdPolicyDto Policy() => new(
        Id: 1,
        BranchCode: "0",
        EnableMaterialRequestOrderFlow: true,
        RequireOpenOrder: false,
        AllowOpenOrderExcess: true,
        AllowMultipleOrdersPerDistribution: true,
        RequireEmployeeUserLink: true,
        AllowFutureDatedDistribution: true,
        RequireManagerApprovalForExcess: false,
        UpdatedBy: null,
        UpdatedDate: null);
}
