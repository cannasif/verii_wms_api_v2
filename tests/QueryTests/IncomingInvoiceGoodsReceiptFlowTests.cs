using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class IncomingInvoiceGoodsReceiptFlowTests
{
    [Fact]
    public async Task E_document_can_be_matched_and_converted_to_idempotent_goods_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();

        var imported = await fixture.Service.ImportAsync(new(
            Fixture.BranchCode,
            fixture.Connection.Id,
            fixture.ELogoInvoice.Uuid.ToString(),
            IncomingInvoiceLookupKind.Automatic,
            true), Fixture.ActorId);

        Assert.Equal(IncomingInvoiceArchiveStatus.NeedsReview, imported.ArchiveStatus);
        Assert.Equal(0, imported.MatchedLineCount);

        var matched = await fixture.Service.MatchAsync(
            imported.Id,
            new(Fixture.BranchCode, fixture.Supplier.Id),
            Fixture.ActorId);
        Assert.Equal(IncomingInvoiceArchiveStatus.ReadyForReceipt, matched.ArchiveStatus);
        Assert.Equal(1, matched.MatchedLineCount);

        var detail = await fixture.Service.GetAsync(imported.Id, Fixture.BranchCode);
        var line = Assert.Single(detail.Lines);
        var request = fixture.CreateReceiptRequest(
            line.Id, electronic: true, "GIB2026AB000001");

        var created = await fixture.Service.CreateGoodsReceiptAsync(
            imported.Id, request, Fixture.ActorId);
        var replayed = await fixture.Service.CreateGoodsReceiptAsync(
            imported.Id, request, Fixture.ActorId);

        Assert.Equal(IncomingInvoiceArchiveStatus.Linked, created.ArchiveStatus);
        Assert.False(created.Replayed);
        Assert.True(replayed.Replayed);
        Assert.Equal(created.GoodsReceiptId, replayed.GoodsReceiptId);
        Assert.Equal(1, fixture.ReceiptOperations.CallCount);
        Assert.NotNull(fixture.ReceiptOperations.LastRequest);
        Assert.Null(fixture.ReceiptOperations.LastRequest.WaybillNo);
        Assert.Equal("GIB2026AB000001", fixture.ReceiptOperations.LastRequest.ElectronicWaybillNo);
        Assert.Equal(fixture.ELogoInvoice.Invoice.InvoiceNo,
            fixture.ReceiptOperations.LastRequest.ShipmentReferenceNo);
        var receiptLine = Assert.Single(fixture.ReceiptOperations.LastRequest.Lines);
        Assert.Equal(24m, receiptLine.Quantity);
        Assert.Equal("ADET", receiptLine.UnitCode);
    }

    [Fact]
    public async Task Ocr_document_is_mapped_and_converted_to_goods_receipt_after_review()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pdf = "%PDF-1.7\ninvoice"u8.ToArray();

        var imported = await fixture.Service.ImportOcrAsync(new(
            Fixture.BranchCode,
            fixture.Supplier.Id,
            "invoice.pdf",
            "application/pdf",
            pdf), Fixture.ActorId);

        Assert.Equal(IncomingInvoiceArchiveStatus.ReadyForReceipt, imported.ArchiveStatus);
        Assert.Equal(1, imported.MatchedLineCount);
        Assert.True(imported.HasPdf);

        var header = await fixture.Db.Set<IncomingInvoiceHeader>()
            .SingleAsync(x => x.Id == imported.Id);
        Assert.Equal(IncomingInvoiceCaptureSource.Ocr, header.CaptureSource);
        Assert.Equal(fixture.Supplier.Id, header.SupplierCustomerId);

        var detail = await fixture.Service.GetAsync(imported.Id, Fixture.BranchCode);
        var line = Assert.Single(detail.Lines);
        var created = await fixture.Service.CreateGoodsReceiptAsync(
            imported.Id,
            fixture.CreateReceiptRequest(
                line.Id, electronic: false, "IRS202600000001"),
            Fixture.ActorId);

        Assert.Equal(IncomingInvoiceArchiveStatus.Linked, created.ArchiveStatus);
        Assert.NotNull(fixture.ReceiptOperations.LastRequest);
        Assert.Equal("IRS202600000001", fixture.ReceiptOperations.LastRequest.WaybillNo);
        Assert.Null(fixture.ReceiptOperations.LastRequest.ElectronicWaybillNo);
        Assert.Equal(24m, Assert.Single(fixture.ReceiptOperations.LastRequest.Lines).Quantity);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string BranchCode = "1";
        public const long ActorId = 42;

        private readonly UnitOfWork _unitOfWork;

        private Fixture(
            WmsDbContext db,
            UnitOfWork unitOfWork,
            Customer supplier,
            ELogoConnection connection,
            ELogoFetchedInvoice eLogoInvoice,
            FakeGoodsReceiptOperations receiptOperations,
            IncomingInvoiceService service)
        {
            Db = db;
            _unitOfWork = unitOfWork;
            Supplier = supplier;
            Connection = connection;
            ELogoInvoice = eLogoInvoice;
            ReceiptOperations = receiptOperations;
            Service = service;
        }

        public WmsDbContext Db { get; }
        public Customer Supplier { get; }
        public ELogoConnection Connection { get; }
        public ELogoFetchedInvoice ELogoInvoice { get; }
        public FakeGoodsReceiptOperations ReceiptOperations { get; }
        public IncomingInvoiceService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new WmsDbContext(options);
            var supplier = new Customer
            {
                BranchCode = BranchCode,
                CustomerCode = "320.001",
                CustomerName = "Test Tedarikçi"
            };
            var stock = new Stock
            {
                BranchCode = BranchCode,
                ErpStockCode = "STK-001",
                StockName = "Sistem Stoğu",
                BaseUnitCode = "ADET"
            };
            var connection = new ELogoConnection
            {
                BranchCode = BranchCode,
                Key = "default",
                DisplayName = "Test eLogo",
                Vkn = "1111111111",
                Username = "test",
                Source = "Test",
                IsActive = true,
                IsDefault = true
            };
            db.AddRange(supplier, stock, connection);
            await db.SaveChangesAsync();

            var unitOfWork = new UnitOfWork(
                db, CreateHttpContextAccessor(BranchCode));
            var audit = new NoopAuditLogWriter();
            var mappingService = new SupplierStockMappingService(unitOfWork, audit);
            await mappingService.CreateAsync(new(
                BranchCode,
                supplier.Id,
                "SUP-001",
                "Tedarikçi stok adı",
                "KOLI",
                stock.Id,
                12m,
                true,
                null,
                null));

            var invoice = CreateParsedInvoice("INV-2026-42");
            var fetched = new ELogoFetchedInvoice(
                connection.Id,
                connection.Vkn,
                Guid.NewGuid(),
                IncomingInvoiceKind.EInvoice,
                "<Invoice />",
                "invoice.xml",
                "%PDF-1.7\ninvoice"u8.ToArray(),
                "invoice.pdf",
                invoice,
                "Test",
                null);
            var receiptOperations = new FakeGoodsReceiptOperations(db);
            var service = new IncomingInvoiceService(
                unitOfWork,
                new FakePostboxClient(fetched),
                new MemoryDocumentStorage(),
                receiptOperations,
                mappingService,
                new FakeOcrClient(invoice),
                audit);
            return new Fixture(
                db, unitOfWork, supplier, connection, fetched, receiptOperations, service);
        }

        public CreateIncomingInvoiceGoodsReceiptRequest CreateReceiptRequest(
            long lineId,
            bool electronic,
            string waybillNo) => new(
                Guid.NewGuid(),
                BranchCode,
                Supplier.Id,
                10,
                20,
                30,
                electronic,
                waybillNo,
                new DateOnly(2026, 7, 30),
                null,
                GoodsReceiptLabelStrategy.None,
                1,
                "Test mal kabul",
                null,
                [new IncomingInvoiceGoodsReceiptLineRequest(lineId, 2m)]);

        public async ValueTask DisposeAsync()
        {
            await _unitOfWork.DisposeAsync();
            await Db.DisposeAsync();
        }

        private static ParsedIncomingInvoice CreateParsedInvoice(string invoiceNo) => new(
            "TEMELFATURA",
            invoiceNo,
            "SATIS",
            new DateOnly(2026, 7, 30),
            null,
            "TRY",
            null,
            "IRS202600000001",
            new ParsedInvoiceParty(
                "2222222222", "Test Tedarikçi", null, null, null, null, null),
            new ParsedInvoiceParty(
                "1111111111", "Test Alıcı", null, null, null, null, null),
            100m,
            100m,
            20m,
            120m,
            0m,
            120m,
            [
                new ParsedIncomingInvoiceLine(
                    1,
                    "1",
                    "SUP-001",
                    null,
                    "Tedarikçi ürünü",
                    null,
                    2m,
                    "KOLI",
                    50m,
                    100m,
                    20m,
                    20m)
            ]);
    }

    private sealed class FakePostboxClient(ELogoFetchedInvoice invoice) : IELogoPostboxClient
    {
        public Task<ELogoFetchedInvoice> FetchAsync(
            long connectionId,
            string branchCode,
            string uuid,
            IncomingInvoiceLookupKind kind,
            bool includePdf,
            CancellationToken ct = default) => Task.FromResult(invoice);
    }

    private sealed class FakeOcrClient(ParsedIncomingInvoice invoice) : IIncomingInvoiceOcrClient
    {
        public IncomingInvoiceOcrStatus Status { get; } = new(
            true,
            "Test OCR",
            "Hazır",
            ["application/pdf", "image/png", "image/jpeg", "image/tiff"],
            20 * 1024 * 1024);

        public Task<OcrAnalyzedInvoice> AnalyzeAsync(
            byte[] content,
            string contentType,
            CancellationToken ct = default) =>
            Task.FromResult(new OcrAnalyzedInvoice(
                invoice, 0.97m, [0.95m], "ocr-operation-1"));
    }

    private sealed class MemoryDocumentStorage : IIncomingInvoiceDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public Task<string> SaveAsync(
            long invoiceId,
            byte[] content,
            string contentType,
            CancellationToken ct = default)
        {
            var path = $"{invoiceId}/{Guid.NewGuid():N}";
            _files[path] = content.ToArray();
            return Task.FromResult(path);
        }

        public Task<Stream> OpenReadAsync(
            string storagePath,
            CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(_files[storagePath], writable: false));

        public void Delete(string storagePath) => _files.Remove(storagePath);
    }

    private sealed class FakeGoodsReceiptOperations(WmsDbContext db)
        : IGoodsReceiptOperationsService
    {
        public int CallCount { get; private set; }
        public CreateManualGoodsReceiptRequest? LastRequest { get; private set; }

        public async Task<ManualGoodsReceiptResult> CreateOrderlessTaskAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            var header = new GoodsReceiptHeader
            {
                BranchCode = request.BranchCode,
                DocumentSeriesId = request.DocumentSeriesId,
                DocumentNo = $"GR-{CallCount:0000}",
                DocumentDate = request.DocumentDate,
                ReceiptType = GoodsReceiptType.Direct,
                InitiationMode = GoodsReceiptInitiationMode.UnplannedTask,
                ProcessType = GoodsReceiptProcessType.OrderlessTask,
                SupplierId = request.SupplierId,
                TargetWarehouseId = request.TargetWarehouseId,
                ReceivingLocationId = request.ReceivingLocationId,
                Status = WarehouseOperationStatus.Released,
                WaybillNo = request.WaybillNo,
                WaybillDate = request.WaybillDate,
                ElectronicWaybillNo = request.ElectronicWaybillNo,
                CreatedBy = actorUserId
            };
            db.Add(header);
            await db.SaveChangesAsync(cancellationToken);
            var lines = request.Lines.Select((line, index) => new GoodsReceiptLine
            {
                BranchCode = request.BranchCode,
                GrHeaderId = header.Id,
                LineNo = index + 1,
                StockId = line.StockId,
                StockCodeSnapshot = $"STK-{line.StockId}",
                UnitCode = line.UnitCode ?? "ADET",
                BaseUnitCode = line.UnitCode ?? "ADET",
                ExpectedQuantity = line.Quantity,
                TargetWarehouseId = line.TargetWarehouseId ?? request.TargetWarehouseId,
                DefaultReceivingLocationId =
                    line.ReceivingLocationId ?? request.ReceivingLocationId
            }).ToList();
            db.AddRange(lines);
            var task = new GoodsReceiptTask
            {
                BranchCode = request.BranchCode,
                GrHeaderId = header.Id,
                TaskNo = $"TASK-{CallCount:0000}",
                Status = GoodsReceiptTaskStatus.Released,
                WarehouseId = request.TargetWarehouseId,
                CreatedBy = actorUserId
            };
            db.Add(task);
            await db.SaveChangesAsync(cancellationToken);
            return new ManualGoodsReceiptResult(
                header.Id,
                header.DocumentNo,
                header.InitiationMode,
                header.Status,
                task.Id,
                task.TaskNo,
                null,
                null,
                null,
                lines.Count,
                lines.Sum(x => x.ExpectedQuantity),
                false,
                []);
        }

        public Task<GoodsReceiptQualityRequirementResult> ResolveQualityRequirementsAsync(
            ResolveGoodsReceiptQualityRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManualGoodsReceiptResult> CreateDirectReceiptAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManualGoodsReceiptResult> CreateDirectReceiptDeferredErpAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            bool qualityAlreadyApproved,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResponse<GoodsReceiptGridRow>> GetPagedAsync(
            PagedRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GoodsReceiptDetail> GetDetailAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static HttpContextAccessor CreateHttpContextAccessor(string branchCode)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Fixture.ActorId.ToString()),
                    new Claim(JwtTokenIssuer.BranchCodeClaim, branchCode)
                ],
                "Test"))
        };
        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            AuditLogWriteEntry entry,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
