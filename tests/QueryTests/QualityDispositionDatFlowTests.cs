using System.Reflection;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityDispositionDatFlowTests
{
    [Fact]
    public void Quality_DAT_movement_is_a_single_direct_source_to_target_transfer()
    {
        var header = new WarehouseTransferHeader
        {
            Id = 91,
            DocumentNo = "DAT-QC-91",
            BusinessContext = WarehouseTransferBusinessContext.QualityDisposition
        };
        header.Lines.Add(new WarehouseTransferLine
        {
            LineNo = 1,
            StockId = 10,
            RequestedQuantity = 2,
            SourceWarehouseId = 100,
            TargetWarehouseId = 200,
            DefaultSourceLocationId = 1001,
            DefaultTargetLocationId = 2001,
            UnitCode = "ADET",
            SourceStockStatus = "QualityHold",
            TargetStockStatus = "Available"
        });
        var trackedLine = new WarehouseTransferLine
        {
            LineNo = 2,
            StockId = 20,
            RequestedQuantity = 1,
            SourceWarehouseId = 100,
            TargetWarehouseId = 200,
            DefaultSourceLocationId = 1002,
            DefaultTargetLocationId = 2002,
            UnitCode = "ADET",
            SourceStockStatus = "QualityHold",
            TargetStockStatus = "Quarantine"
        };
        trackedLine.Trackings.Add(new WarehouseTransferTracking
        {
            PlannedQuantity = 1,
            LotNo = "LOT-1",
            SerialNo = "SER-1",
            SourceLocationId = 1003,
            TargetLocationId = 2003
        });
        header.Lines.Add(trackedLine);

        var request = WarehouseTransferOperationService.BuildQualityDispositionMovementRequest(
            header,
            Guid.Parse("dd58aa00-66e2-4f1d-b444-72b00e88c718"));

        Assert.Equal("Transfer", request.OperationType);
        Assert.Equal("WarehouseTransfer", request.ReferenceType);
        Assert.Equal(2, request.Lines.Count);
        Assert.Collection(request.Lines,
            line =>
            {
                Assert.Equal(100, line.SourceWarehouseId);
                Assert.Equal(1001, line.SourceLocationId);
                Assert.Equal(200, line.TargetWarehouseId);
                Assert.Equal(2001, line.TargetLocationId);
                Assert.Equal("QualityHold", line.SourceStockStatus);
                Assert.Equal("Available", line.TargetStockStatus);
            },
            line =>
            {
                Assert.Equal("LOT-1", line.LotNo);
                Assert.Equal("SER-1", line.SerialNo);
                Assert.Equal(1003, line.SourceLocationId);
                Assert.Equal(2003, line.TargetLocationId);
                Assert.Equal("Quarantine", line.TargetStockStatus);
            });
    }

    [Fact]
    public void Quality_DAT_retry_keys_are_stable_and_phase_specific()
    {
        var first = QualityDispositionDatJob.CreateIdempotencyKey(17, "stock-completion");
        var replay = QualityDispositionDatJob.CreateIdempotencyKey(17, "stock-completion");
        var erp = QualityDispositionDatJob.CreateIdempotencyKey(17, "erp-posting");

        Assert.Equal(first, replay);
        Assert.NotEqual(first, erp);
        Assert.NotEqual(Guid.Empty, first);
    }

    [Fact]
    public void Quality_DAT_follow_up_has_bounded_retry_and_distributed_concurrency_guards()
    {
        var process = typeof(IGoodsReceiptErpSuccessJob).GetMethod(
            nameof(IGoodsReceiptErpSuccessJob.ProcessGoodsReceiptAsync));
        Assert.NotNull(process);
        var retry = process.GetCustomAttribute<AutomaticRetryAttribute>();
        var concurrency = process.GetCustomAttribute<DisableConcurrentExecutionAttribute>();

        Assert.NotNull(retry);
        Assert.Equal(5, retry!.Attempts);
        Assert.NotNull(concurrency);
    }

    [Fact]
    public void Background_actor_falls_back_to_the_receipt_audit_owner()
    {
        var receipt = new GoodsReceiptHeader
        {
            ReceivedBy = 44,
            UpdatedBy = 33,
            CreatedBy = 22,
            ErpIntegrationStatus = ErpIntegrationStatus.Succeeded
        };

        Assert.Equal(99, QualityDispositionDatJob.ResolveActor(receipt, 99));
        Assert.Equal(44, QualityDispositionDatJob.ResolveActor(receipt, 0));
    }

    [Fact]
    public async Task DAT_stock_completion_and_ERP_posting_wait_for_receipt_ERP_success()
    {
        await using var db = CreateDbContext();
        var receipt = new GoodsReceiptHeader
        {
            BranchCode = "0",
            DocumentNo = "GR-1",
            ErpIntegrationStatus = ErpIntegrationStatus.Pending,
            ReceivedBy = 72
        };
        var inspection = new QualityInspection
        {
            BranchCode = "0",
            InspectionNo = "QC-1",
            SourceDocumentType = "GoodsReceipt",
            SourceDocumentNo = "GR-1"
        };
        var inspectionLine = new QualityInspectionLine
        {
            BranchCode = "0",
            Inspection = inspection,
            StockId = 10,
            StockCodeSnapshot = "STK-1",
            Quantity = 1,
            SampleQuantity = 1
        };
        inspection.Lines.Add(inspectionLine);
        var transfer = new WarehouseTransferHeader
        {
            BranchCode = "0",
            DocumentNo = "DAT-QC-1",
            BusinessContext = WarehouseTransferBusinessContext.QualityDisposition,
            Status = WarehouseTransferStatus.Draft,
            ErpIntegrationStatus = ErpIntegrationStatus.Pending
        };
        db.AddRange(receipt, inspection, transfer);
        await db.SaveChangesAsync();
        inspection.SourceDocumentId = receipt.Id;
        db.QualityInspectionDispositions.Add(new QualityInspectionDisposition
        {
            BranchCode = "0",
            QualityInspection = inspection,
            QualityInspectionLine = inspectionLine,
            WarehouseTransferId = transfer.Id,
            IdempotencyKey = Guid.NewGuid(),
            SequenceNo = 1,
            Quantity = 1,
            SourceWarehouseId = 1,
            SourceLocationId = 11,
            TargetWarehouseId = 2,
            TargetLocationId = 22,
            DecisionBy = 72,
            DecisionAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var events = new List<string>();
        var operations = new RecordingTransferOperations(db, events);
        var erp = new RecordingErpPostingService(db, events);
        var job = new QualityDispositionDatJob(
            new UnitOfWork(db, new HttpContextAccessor()),
            operations,
            erp,
            new RecordingGoodsReceiptCoordinator(db, events),
            NullLogger<QualityDispositionDatJob>.Instance);

        await job.ProcessGoodsReceiptAsync(receipt.Id, 72);
        Assert.Empty(events);

        receipt.ErpIntegrationStatus = ErpIntegrationStatus.Succeeded;
        await db.SaveChangesAsync();
        await job.ProcessGoodsReceiptAsync(receipt.Id, 72);

        Assert.Equal(["complete", "erp"], events);
        Assert.Equal(WarehouseTransferStatus.Completed, transfer.Status);
        Assert.Equal(ErpIntegrationStatus.Succeeded, transfer.ErpIntegrationStatus);

        await job.ProcessGoodsReceiptAsync(receipt.Id, 72);
        Assert.Equal(["complete", "erp"], events);
    }

    [Fact]
    public async Task Recovery_posts_a_conclusively_quarantined_receipt_before_its_DAT()
    {
        await using var db = CreateDbContext();
        var receipt = new GoodsReceiptHeader
        {
            BranchCode = "0",
            DocumentNo = "GR-QUARANTINE-1",
            Status = WarehouseOperationStatus.Processed,
            ApprovalStatus = OperationApprovalStatus.NotRequired,
            QualityStatus = OperationQualityStatus.InProgress,
            ErpPostingPolicy = GoodsReceiptErpPostingPolicy.AfterAllApprovals,
            ErpIntegrationStatus = ErpIntegrationStatus.Pending,
            ReceivedBy = 72
        };
        var inspection = new QualityInspection
        {
            BranchCode = "0",
            InspectionNo = "QC-QUARANTINE-1",
            SourceDocumentType = "GoodsReceipt",
            SourceDocumentNo = receipt.DocumentNo,
            Status = QualityInspectionStatus.Quarantined,
            DecidedAtUtc = DateTimeOffset.UtcNow
        };
        var inspectionLine = new QualityInspectionLine
        {
            BranchCode = "0",
            Inspection = inspection,
            StockId = 10,
            StockCodeSnapshot = "STK-1",
            Quantity = 1,
            SampleQuantity = 1,
            QuarantineQuantity = 1,
            Decision = QualityDecision.Quarantined,
            DecisionAtUtc = inspection.DecidedAtUtc
        };
        inspection.Lines.Add(inspectionLine);
        var transfer = new WarehouseTransferHeader
        {
            BranchCode = "0",
            DocumentNo = "DAT-QC-QUARANTINE-1",
            BusinessContext = WarehouseTransferBusinessContext.QualityDisposition,
            Status = WarehouseTransferStatus.Draft,
            ErpIntegrationStatus = ErpIntegrationStatus.Pending
        };
        db.AddRange(receipt, inspection, transfer);
        await db.SaveChangesAsync();
        inspection.SourceDocumentId = receipt.Id;
        db.QualityInspectionDispositions.Add(new QualityInspectionDisposition
        {
            BranchCode = "0",
            QualityInspection = inspection,
            QualityInspectionLine = inspectionLine,
            WarehouseTransferId = transfer.Id,
            IdempotencyKey = Guid.NewGuid(),
            SequenceNo = 1,
            Decision = QualityDecision.Quarantined,
            Quantity = 1,
            SourceWarehouseId = 1,
            SourceLocationId = 11,
            TargetWarehouseId = 2,
            TargetLocationId = 22,
            DecisionBy = 72,
            DecisionAtUtc = inspection.DecidedAtUtc.Value
        });
        await db.SaveChangesAsync();

        var events = new List<string>();
        var job = new QualityDispositionDatJob(
            new UnitOfWork(db, new HttpContextAccessor()),
            new RecordingTransferOperations(db, events),
            new RecordingErpPostingService(db, events),
            new RecordingGoodsReceiptCoordinator(db, events),
            NullLogger<QualityDispositionDatJob>.Instance);

        await job.RetryPendingAsync();

        Assert.Equal(["receipt-erp"], events);
        Assert.Equal(ErpIntegrationStatus.Succeeded, receipt.ErpIntegrationStatus);
        Assert.Equal(WarehouseTransferStatus.Draft, transfer.Status);

        await job.RetryPendingAsync();

        Assert.Equal(["receipt-erp", "complete", "erp"], events);
        Assert.Equal(WarehouseTransferStatus.Completed, transfer.Status);
        Assert.Equal(ErpIntegrationStatus.Succeeded, transfer.ErpIntegrationStatus);
    }

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new WmsDbContext(options);
    }

    private sealed class RecordingTransferOperations(WmsDbContext db, List<string> events)
        : IWarehouseTransferOperationService
    {
        public async Task<WarehouseTransferOperationResult> CompleteQualityDispositionAsync(
            long id, Guid idempotencyKey, long actor, CancellationToken ct = default)
        {
            events.Add("complete");
            var header = await db.WarehouseTransferHeaders.SingleAsync(x => x.Id == id, ct);
            header.Status = WarehouseTransferStatus.Completed;
            header.CompletedBy = actor;
            header.CompletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Result(header);
        }

        public Task<WarehouseTransferOperationResult> ApproveAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> ReleaseAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> PickAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> DispatchAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> ReceiveAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> PutawayAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> CancelAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) => Unsupported();
        public Task<WarehouseTransferOperationResult> CancelAfterErpDeletionAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) => Unsupported();

        private static WarehouseTransferOperationResult Result(WarehouseTransferHeader header) =>
            new(header.Id, header.DocumentNo, header.Status.ToString(), null, 0, 0, 0, 0, false);
        private static Task<WarehouseTransferOperationResult> Unsupported() =>
            throw new NotSupportedException();
    }

    private sealed class RecordingErpPostingService(WmsDbContext db, List<string> events) : IErpPostingService
    {
        public async Task<ErpPostingResult> PostWarehouseTransferAsync(
            long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken)
        {
            var header = await db.WarehouseTransferHeaders.SingleAsync(x => x.Id == id, cancellationToken);
            Assert.Equal(WarehouseTransferStatus.Completed, header.Status);
            events.Add("erp");
            header.ErpIntegrationStatus = ErpIntegrationStatus.Succeeded;
            await db.SaveChangesAsync(cancellationToken);
            return Success(ErpPostingSourceType.WarehouseTransfer, id, header.DocumentNo);
        }

        public Task<ErpPostingResult> PostGoodsReceiptAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken) => Unsupported();
        public Task<ErpPostingResult> PostWarehouseInboundAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken) => Unsupported();
        public Task<ErpPostingResult> PostWarehouseOutboundAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken) => Unsupported();
        public Task<ErpPostingResult> PostShipmentAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken) => Unsupported();
        public Task<ErpPostingResult> GetAsync(ErpPostingSourceType sourceType, long sourceEntityId, CancellationToken cancellationToken) => Unsupported();
        public Task<ErpPostingResult> ReconcileAsync(ErpPostingSourceType sourceType, long sourceEntityId, ReconcileErpPostingRequest request, long userId, CancellationToken cancellationToken) => Unsupported();

        private static ErpPostingResult Success(ErpPostingSourceType type, long id, string no) =>
            new(1, type, id, no, ErpPostingStatus.Succeeded, 1, no, null, null, null, null, null, DateTimeOffset.UtcNow);
        private static Task<ErpPostingResult> Unsupported() => throw new NotSupportedException();
    }

    private sealed class RecordingGoodsReceiptCoordinator(WmsDbContext db, List<string> events)
        : IGoodsReceiptErpPostingCoordinator
    {
        public async Task<ErpPostingResult?> PostIfEligibleAsync(
            long goodsReceiptId,
            long actorUserId,
            CancellationToken cancellationToken)
        {
            var receipt = await db.GoodsReceiptHeaders.SingleAsync(
                x => x.Id == goodsReceiptId,
                cancellationToken);
            events.Add("receipt-erp");
            receipt.ErpIntegrationStatus = ErpIntegrationStatus.Succeeded;
            await db.SaveChangesAsync(cancellationToken);
            return new ErpPostingResult(
                1,
                ErpPostingSourceType.GoodsReceipt,
                receipt.Id,
                receipt.DocumentNo,
                ErpPostingStatus.Succeeded,
                1,
                receipt.DocumentNo,
                null,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow);
        }
    }
}
