using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityQuantityDecisionTests
{
    [Fact]
    public void Terminal_quality_decision_synchronizes_goods_receipt_operation_status()
    {
        var receipt = new GoodsReceiptHeader
        {
            Status = WarehouseOperationStatus.Processed,
            QualityStatus = OperationQualityStatus.Passed,
            RequirePutaway = false,
            Lines =
            [
                new GoodsReceiptLine
                {
                    ExpectedQuantity = 10,
                    ReceivedQuantity = 10,
                    AcceptedQuantity = 10
                }
            ]
        };

        QualityService.SynchronizeGoodsReceiptStatus(receipt, 42);

        Assert.Equal(WarehouseOperationStatus.Completed, receipt.Status);
        Assert.NotNull(receipt.CompletedAtUtc);
        Assert.Equal(42, receipt.CompletedBy);
    }

    [Fact]
    public void Decision_result_explains_when_receipt_approval_blocks_erp_posting()
    {
        var receipt = new GoodsReceiptHeader
        {
            Id = 15,
            DocumentNo = "GR1202600000015",
            Status = WarehouseOperationStatus.Completed,
            QualityStatus = OperationQualityStatus.Passed,
            ApprovalStatus = OperationApprovalStatus.Pending,
            ErpIntegrationStatus = ErpIntegrationStatus.Pending,
            ErpPostingPolicy = GoodsReceiptErpPostingPolicy.AfterAllApprovals
        };

        var result = QualityService.BuildDecisionResult(receipt, null);

        Assert.False(result.ErpDocumentCreatedNow);
        Assert.Contains("mal kabul onayı bekleniyor", result.Message);
    }

    [Fact]
    public void Decision_result_reports_persisted_erp_success_status()
    {
        var receipt = new GoodsReceiptHeader
        {
            Id = 16,
            DocumentNo = "GR1202600000016",
            WaybillNo = "IRS202600000001",
            ElectronicWaybillNo = "GIB2026AB000001",
            Status = WarehouseOperationStatus.Completed,
            QualityStatus = OperationQualityStatus.Passed,
            ApprovalStatus = OperationApprovalStatus.Approved,
            ErpIntegrationStatus = ErpIntegrationStatus.Succeeded,
            ErpPostingPolicy = GoodsReceiptErpPostingPolicy.AfterAllApprovals
        };

        var result = QualityService.BuildDecisionResult(receipt, null);

        Assert.Equal(ErpIntegrationStatus.Succeeded, result.ErpIntegrationStatus);
        Assert.Equal("IRS202600000001", result.GoodsReceiptWaybillNo);
        Assert.Equal("GIB2026AB000001", result.GoodsReceiptElectronicWaybillNo);
        Assert.False(result.ErpDocumentCreatedNow);
        Assert.Contains("daha önce oluşturulmuş", result.Message);
    }

    [Fact]
    public void Decision_result_keeps_quality_success_when_erp_follow_up_fails()
    {
        var receipt = new GoodsReceiptHeader
        {
            Id = 17,
            DocumentNo = "GR1202600000017",
            Status = WarehouseOperationStatus.Completed,
            QualityStatus = OperationQualityStatus.Passed,
            ApprovalStatus = OperationApprovalStatus.Approved,
            ErpIntegrationStatus = ErpIntegrationStatus.Failed,
            ErpPostingPolicy = GoodsReceiptErpPostingPolicy.AfterQualityApproval
        };

        var result = QualityService.BuildDecisionResult(
            receipt,
            null,
            "Netsis REST oturumu açılamadı.");

        Assert.False(result.ErpDocumentCreatedNow);
        Assert.Contains("Kalite kararı uygulandı", result.Message);
        Assert.Contains("Netsis", result.Message);
    }

    [Fact]
    public void Decision_result_appends_dat_follow_up_failure_after_erp_success()
    {
        var receipt = new GoodsReceiptHeader
        {
            Id = 18,
            DocumentNo = "GR1202600000018",
            Status = WarehouseOperationStatus.Completed,
            QualityStatus = OperationQualityStatus.Failed,
            ApprovalStatus = OperationApprovalStatus.Approved,
            ErpIntegrationStatus = ErpIntegrationStatus.Succeeded,
            ErpPostingPolicy = GoodsReceiptErpPostingPolicy.AfterQualityApproval
        };

        var result = QualityService.BuildDecisionResult(
            receipt,
            null,
            null,
            "Quality DAT ERP posting failed.");

        Assert.Contains("daha önce oluşturulmuş", result.Message);
        Assert.Contains("kalite DAT otomatik tamamlanamadı", result.Message);
        Assert.Contains("Quality DAT ERP posting failed.", result.Message);
    }

    [Fact]
    public void Ten_units_can_be_split_into_five_accepted_and_five_quarantined()
    {
        var line = PendingLine(10);
        var receiptLine = new GoodsReceiptLine
        {
            ReceivedQuantity = 10,
            QuarantineQuantity = 10
        };
        var allocations = new Dictionary<long, QualityInspectionQuantityDecisionRequest>
        {
            [line.Id] = new(line.Id, 5, 0, 5)
        };

        var parts = QualityService.BuildDecisionParts([line], allocations, QualityDecision.Pending);
        QualityService.ApplyDecisionParts(
            line, receiptLine, parts, 42, DateTimeOffset.UtcNow, "5 kabul, 5 karantina");
        var state = QualityService.ResolveDecisionState([line], releasesQuarantine: false);

        Assert.Equal(5, line.AcceptedQuantity);
        Assert.Equal(5, line.QuarantineQuantity);
        Assert.Equal(QualityDecision.Quarantined, line.Decision);
        Assert.Equal(5, receiptLine.AcceptedQuantity);
        Assert.Equal(5, receiptLine.QuarantineQuantity);
        Assert.Equal(0, receiptLine.RejectedQuantity);
        Assert.Equal(QualityInspectionStatus.Quarantined, state.InspectionStatus);
        Assert.Equal(OperationQualityStatus.InProgress, state.ReceiptStatus);
        Assert.True(state.IsTerminal);
    }

    [Fact]
    public void Quarantined_remainder_can_later_be_released_without_moving_accepted_quantity_again()
    {
        var line = PendingLine(10);
        line.AcceptedQuantity = 5;
        line.QuarantineQuantity = 5;
        line.Decision = QualityDecision.Quarantined;
        var receiptLine = new GoodsReceiptLine
        {
            ReceivedQuantity = 10,
            AcceptedQuantity = 5,
            QuarantineQuantity = 5
        };
        var allocations = new Dictionary<long, QualityInspectionQuantityDecisionRequest>
        {
            [line.Id] = new(line.Id, 5, 0, 0)
        };

        var parts = QualityService.BuildDecisionParts([line], allocations, QualityDecision.Pending);
        QualityService.ApplyDecisionParts(
            line, receiptLine, parts, 42, DateTimeOffset.UtcNow, "Karantina serbest bırakıldı");
        var state = QualityService.ResolveDecisionState([line], releasesQuarantine: true);

        Assert.Equal(10, line.AcceptedQuantity);
        Assert.Equal(0, line.QuarantineQuantity);
        Assert.Equal(10, receiptLine.AcceptedQuantity);
        Assert.Equal(0, receiptLine.QuarantineQuantity);
        Assert.Equal(QualityInspectionStatus.Released, state.InspectionStatus);
        Assert.Equal(OperationQualityStatus.Passed, state.ReceiptStatus);
    }

    [Fact]
    public void Allocation_total_must_equal_the_actionable_quantity()
    {
        var line = PendingLine(10);
        var allocations = new Dictionary<long, QualityInspectionQuantityDecisionRequest>
        {
            [line.Id] = new(line.Id, 4, 0, 5)
        };

        var error = Assert.Throws<AppException>(() =>
            QualityService.BuildDecisionParts([line], allocations, QualityDecision.Pending));

        Assert.Contains("10", error.Message);
    }

    [Fact]
    public void Available_stock_is_reclassified_instead_of_double_counted_when_quality_hold_is_disabled()
    {
        var line = PendingLine(10);
        var receiptLine = new GoodsReceiptLine
        {
            ReceivedQuantity = 10,
            AcceptedQuantity = 10,
            QuarantineQuantity = 0
        };
        var allocations = new Dictionary<long, QualityInspectionQuantityDecisionRequest>
        {
            [line.Id] = new(line.Id, 5, 0, 5)
        };

        var parts = QualityService.BuildDecisionParts([line], allocations, QualityDecision.Pending);
        QualityService.ApplyDecisionParts(
            line, receiptLine, parts, 42, DateTimeOffset.UtcNow, "PARTIAL", null);

        Assert.Equal(5, receiptLine.AcceptedQuantity);
        Assert.Equal(5, receiptLine.QuarantineQuantity);
        Assert.Equal(10, receiptLine.AcceptedQuantity + receiptLine.RejectedQuantity + receiptLine.QuarantineQuantity);
    }

    [Fact]
    public void A_quarantine_remainder_stays_actionable_even_when_another_part_is_rejected()
    {
        var line = PendingLine(10);
        var receiptLine = new GoodsReceiptLine
        {
            ReceivedQuantity = 10,
            QuarantineQuantity = 10
        };
        var allocations = new Dictionary<long, QualityInspectionQuantityDecisionRequest>
        {
            [line.Id] = new(line.Id, 0, 5, 5)
        };

        var parts = QualityService.BuildDecisionParts([line], allocations, QualityDecision.Pending);
        QualityService.ApplyDecisionParts(
            line, receiptLine, parts, 42, DateTimeOffset.UtcNow, "MIXED", null);
        var state = QualityService.ResolveDecisionState([line], releasesQuarantine: false);

        Assert.Equal(QualityDecision.Quarantined, line.Decision);
        Assert.Equal(QualityInspectionStatus.Quarantined, state.InspectionStatus);
        Assert.Equal(OperationQualityStatus.InProgress, state.ReceiptStatus);
    }

    private static QualityInspectionLine PendingLine(decimal quantity) => new()
    {
        Id = 7,
        StockId = 70,
        StockCodeSnapshot = "STOK-70",
        Quantity = quantity,
        Decision = QualityDecision.Pending
    };
}
