using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProjectSettings.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Api;
using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Host.Serialization;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelVehicleUnknownPlateIntegrationTests
{
    private const string ResolvePermission = "WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE";
    private const string VehicleManagePermission = "WMS.STEEL_RECEIPT.VEHICLE.MANAGE";

    [Fact]
    public async Task Import_commit_replays_before_duplicate_detection_but_new_key_conflicts()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"steel-import-idempotency-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var context = new WmsDbContext(options);
        const string branch = "IDEMP";
        var supplier = new Customer
        {
            BranchCode = branch,
            BusinessUnitCode = 1,
            CustomerCode = "IDEMP-SUP",
            CustomerName = "Idempotency supplier"
        };
        var warehouse = new Warehouse
        {
            BranchCode = branch,
            WarehouseCode = 64_001,
            WarehouseName = "Idempotency warehouse"
        };
        var stock = new Stock
        {
            BranchCode = branch,
            BusinessUnitCode = 1,
            ErpStockCode = "IDEMP-STOCK",
            StockName = "Steel plate",
            BaseUnitCode = "ADET"
        };
        var series = new DocumentSeries
        {
            BranchCode = branch,
            Code = "IDEMP-GR",
            Name = "Idempotency receipt",
            DocumentType = WmsDocumentType.GoodsReceipt,
            Prefix = "ID",
            NumberLength = 8,
            StartNumber = 1,
            NextNumber = 1,
            IsActive = true
        };
        context.AddRange(supplier, warehouse, stock, series);
        await context.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = branch,
            WarehouseId = warehouse.Id,
            Code = "IDEMP-REC",
            Name = "Receiving",
            LocationType = LocationTypes.Receiving,
            IsActive = true,
            IsPutaway = true
        };
        context.Add(location);
        await context.SaveChangesAsync();

        var http = new HttpContextAccessor();
        await using var uow = new UnitOfWork(context, http);
        var service = new SteelReceiptService(
            uow,
            new FakeGoodsReceiptOperations(context),
            new NullErpPostingCoordinator(),
            new UnusedStockMovementService(),
            new NullAuditWriter(),
            new MemoryStorage());
        var import = new PreviewSteelReceiptImportRequest(
            branch,
            "IDEMP-IMPORT",
            "idempotency.xlsx",
            null,
            null,
            supplier.Id,
            warehouse.Id,
            location.Id,
            series.Id,
            "GIB2026AB000001",
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            [
                new SteelImportLineRequest(
                    1, null, null, stock.Id, stock.ErpStockCode,
                    null, null, "SER-IDEMP-1", null, 1, "ADET",
                    null, null, null, null, null, null)
            ]);
        var key = Guid.NewGuid();
        var request = new CommitSteelReceiptImportRequest(key, import);

        var createdPlanId = await service.CommitAsync(request, actor: 1);
        var replayedPlanId = await service.CommitAsync(request, actor: 1);

        Assert.Equal(createdPlanId, replayedPlanId);
        Assert.Single(await context.Set<SteelReceiptPlan>().ToListAsync());
        Assert.Single(await context.Set<SteelReceiptPlanLine>().ToListAsync());

        var duplicate = await Assert.ThrowsAsync<AppException>(() =>
            service.CommitAsync(request with { IdempotencyKey = Guid.NewGuid() }, actor: 1));
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.Contains("daha önce", duplicate.Message);
    }

    [Fact]
    public async Task Initial_known_acceptance_requires_target_warehouse_access()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new CompleteSteelVehicleAcceptanceRequest(
            Guid.NewGuid(),
            new SaveVehicleCheckInRequest(
                null, null, fixture.Branch, "34 DENIED 34", null, null, null, null,
                null, 1, fixture.Supplier.Id, null),
            [
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    fixture.Lines[0].Id,
                    fixture.Lines[0].ReceivingLocationId,
                    Convert.ToBase64String(fixture.Lines[0].RowVersion),
                    null)
            ],
            null);

        var denied = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Acceptance.CompleteAsync(
                request,
                [new VehicleImageUpload(
                    new MemoryStream([1]), "vehicle.jpg", "image/jpeg", 1)],
                [],
                fixture.WarehouseDeniedManager.Id));

        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        Assert.Empty(await fixture.Context.Set<SteelVehicleAcceptance>().ToListAsync());
    }

    [Fact]
    public async Task Existing_vehicle_can_append_slots_and_old_unknown_controls_conversion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstKey = Guid.NewGuid();
        var firstRequest = new CompleteSteelVehicleAcceptanceRequest(
            firstKey,
            new SaveVehicleCheckInRequest(
                null, null, fixture.Branch, "34 APPEND 34", null, null, null, null,
                null, 2, fixture.Supplier.Id, null),
            [
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    fixture.Lines[0].Id,
                    fixture.Lines[0].ReceivingLocationId,
                    Convert.ToBase64String(fixture.Lines[0].RowVersion),
                    null),
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Unknown, null, null, null, null)
            ],
            null);
        var first = await fixture.Acceptance.CompleteAsync(
            firstRequest,
            [new VehicleImageUpload(new MemoryStream([1]), "vehicle.jpg", "image/jpeg", 1)],
            [],
            fixture.Creator.Id);
        Assert.Equal(2, first.Plates.Count);
        Assert.Equal(1, first.UnknownCount);

        var appendKey = Guid.NewGuid();
        var appendRequest = new CompleteSteelVehicleAcceptanceRequest(
            appendKey,
            new SaveVehicleCheckInRequest(
                first.Vehicle.Header.Id,
                first.Vehicle.Header.RowVersion,
                fixture.Branch,
                first.Vehicle.Header.PlateNo,
                null, null, null, null, null,
                3,
                null,
                null),
            [
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    fixture.Lines[1].Id,
                    fixture.Lines[1].ReceivingLocationId,
                    Convert.ToBase64String(fixture.Lines[1].RowVersion),
                    null)
            ],
            null);
        var appended = await fixture.Acceptance.CompleteAsync(
            appendRequest, [], [], fixture.Creator.Id);
        Assert.Equal(3, appended.Plates.Count);
        Assert.Equal(1, appended.UnknownCount);
        Assert.Equal(fixture.Supplier.Id, appended.Vehicle.Header.CustomerId);
        Assert.Equal(
            VehicleCheckInStatus.ContainsUnknownPlates.ToString(),
            appended.Vehicle.Header.Status);

        var replay = await fixture.Acceptance.CompleteAsync(
            appendRequest, [], [], fixture.Creator.Id);
        Assert.True(replay.Replayed);
        Assert.Equal(3, replay.Plates.Count);
        Assert.Equal(3, await fixture.Context.Set<SteelVehicleAcceptedPlate>()
            .CountAsync(x => x.VehicleCheckInId == first.Vehicle.Header.Id));

        var convertedKnown = await fixture.SteelReceipt.ConvertAsync(
            fixture.Plan.Id,
            fixture.ConvertRequest([
                fixture.Lines[0].Id,
                fixture.Lines[1].Id
            ]),
            fixture.Creator.Id);
        Assert.Equal(2, convertedKnown.ConvertedLineCount);

        var oldUnknown = appended.Plates.Single(
            x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown));
        Assert.True(oldUnknown.CanResolve);
        await fixture.Acceptance.ResolveUnknownPlateAsync(
            oldUnknown.Id,
            fixture.ResolveRequest(oldUnknown, fixture.Lines[2]),
            fixture.ResolveImages(fixture.Lines[2]),
            fixture.Creator.Id);

        var resolved = await fixture.Acceptance.GetLatestByVehicleAsync(
            first.Vehicle.Header.Id, canManageVehicleAcceptance: true);
        Assert.Equal(3, resolved!.Plates.Count);
        Assert.Equal(0, resolved.UnknownCount);
        var converted = await fixture.SteelReceipt.ConvertAsync(
            fixture.Plan.Id,
            fixture.ConvertRequest([fixture.Lines[2].Id]),
            fixture.Creator.Id);
        Assert.Equal(1, converted.ConvertedLineCount);

        var duplicateRequest = appendRequest with
        {
            IdempotencyKey = Guid.NewGuid(),
            Vehicle = appendRequest.Vehicle with
            {
                RowVersion = resolved.Vehicle.Header.RowVersion,
                SteelSheetCount = 4
            },
            Slots =
            [
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    fixture.Lines[1].Id,
                    fixture.Lines[1].ReceivingLocationId,
                    Convert.ToBase64String(fixture.Lines[1].RowVersion),
                    null)
            ]
        };
        var duplicate = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Acceptance.CompleteAsync(
                duplicateRequest, [], [], fixture.Creator.Id));
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task All_unknown_acceptance_does_not_require_a_supplier()
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseRequest = fixture.CompleteRequest();
        var request = baseRequest with
        {
            IdempotencyKey = Guid.NewGuid(),
            Vehicle = baseRequest.Vehicle with { CustomerId = null },
            Slots = Enumerable.Range(0, 5)
                .Select(_ => new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Unknown, null, null, null, null))
                .ToArray()
        };
        var vehicleImage = new VehicleImageUpload(
            new MemoryStream([1, 2, 3]), "vehicle.jpg", "image/jpeg", 3);

        var completed = await fixture.Acceptance.CompleteAsync(
            request, [vehicleImage], [], fixture.Creator.Id);

        Assert.Null(completed.Vehicle.Header.CustomerId);
        Assert.Equal(5, completed.UnknownCount);
        Assert.All(
            completed.Plates,
            plate => Assert.Equal(
                nameof(SteelPlateIdentityStatus.Unknown),
                plate.IdentityStatus));

        var firstUnknown = completed.Plates.OrderBy(x => x.SequenceNo).First();
        await fixture.Acceptance.ResolveUnknownPlateAsync(
            firstUnknown.Id,
            fixture.ResolveRequest(firstUnknown, fixture.Lines[0]),
            fixture.ResolveImages(fixture.Lines[0]),
            fixture.Creator.Id);
        var resolved = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: true);

        Assert.Equal(fixture.Supplier.Id, resolved!.Vehicle.Header.CustomerId);
    }

    [Fact]
    public async Task Unknown_plate_lifecycle_is_persisted_authorized_and_conversion_safe()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.CompleteRequest();
        var vehicleImage = new VehicleImageUpload(
            new MemoryStream([1, 2, 3]), "vehicle.jpg", "image/jpeg", 3);

        var completed = await fixture.Acceptance.CompleteAsync(
            request, [vehicleImage], [], fixture.Creator.Id);

        Assert.Equal(5, completed.Plates.Count);
        Assert.Equal(2, completed.UnknownCount);
        Assert.True(completed.ContainsUnknownPlates);
        Assert.True(completed.CanResolveUnknownPlates);

        var persisted = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: true);
        Assert.NotNull(persisted);
        Assert.Equal(5, persisted.Plates.Count);
        Assert.Equal(2, persisted.UnknownCount);
        var knownPlates = persisted.Plates
            .Where(x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Known))
            .ToArray();
        Assert.Equal(3, knownPlates.Length);
        Assert.All(knownPlates, plate =>
        {
            Assert.NotNull(plate.PlanLineSummary);
            Assert.Equal(plate.PlanLineId, plate.PlanLineSummary!.Id);
            Assert.NotEmpty(plate.PlanLineSummary.StockCode);
            Assert.Single(plate.Attachments);
        });
        Assert.All(
            persisted.Plates.Where(x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown)),
            plate =>
            {
                Assert.Null(plate.PlanLineSummary);
                Assert.Empty(plate.Attachments);
            });
        Assert.Equal(
            VehicleCheckInStatus.ContainsUnknownPlates.ToString(),
            persisted.Vehicle.Header.Status);

        var acceptanceEntity = await fixture.Context.Set<SteelVehicleAcceptance>()
            .SingleAsync(x => x.Id == completed.AcceptanceId);
        Assert.Equal(SteelVehicleAcceptanceStatus.PartiallyIdentified, acceptanceEntity.Status);

        var knownLineIds = completed.Plates
            .Where(x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Known))
            .Select(x => x.PlanLineId!.Value)
            .ToArray();
        var convertedKnown = await fixture.SteelReceipt.ConvertAsync(
            fixture.Plan.Id,
            fixture.ConvertRequest(knownLineIds),
            fixture.Creator.Id);
        Assert.Equal(3, convertedKnown.ConvertedLineCount);

        var sameRoleView = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: true);
        Assert.True(sameRoleView!.CanResolveUnknownPlates);
        Assert.All(
            sameRoleView.Plates.Where(x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown)),
            plate => Assert.True(plate.CanResolve));
        var viewOnly = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: false);
        Assert.False(viewOnly!.CanResolveUnknownPlates);
        Assert.All(viewOnly.Plates, plate => Assert.False(plate.CanResolve));

        var unknowns = persisted.Plates
            .Where(x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown))
            .OrderBy(x => x.SequenceNo)
            .ToArray();
        var resolveLine4 = fixture.ResolveRequest(unknowns[0], fixture.Lines[3]);

        Assert.True(await fixture.Permissions.HasPermissionAsync(
            fixture.Principal(fixture.SameRole), ResolvePermission));
        Assert.True(await fixture.Permissions.HasPermissionAsync(
            fixture.Principal(fixture.SameRole), VehicleManagePermission));
        var noPermissionController = fixture.Controller(fixture.NoPermission);
        var permissionDenied = await Assert.ThrowsAsync<AppException>(() =>
            noPermissionController.ResolveUnknownPlate(
                unknowns[0].Id, fixture.ResolveForm(resolveLine4), CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, permissionDenied.StatusCode);

        var warehouseDenied = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Controller(fixture.WarehouseDeniedManager).ResolveUnknownPlate(
                unknowns[0].Id, fixture.ResolveForm(resolveLine4), CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, warehouseDenied.StatusCode);

        await fixture.Controller(fixture.SameRole).ResolveUnknownPlate(
            unknowns[0].Id, fixture.ResolveForm(resolveLine4), CancellationToken.None);
        var afterFirst = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: true);
        Assert.Equal(1, afterFirst!.UnknownCount);
        Assert.True(afterFirst.ContainsUnknownPlates);
        Assert.Equal(
            VehicleCheckInStatus.ContainsUnknownPlates.ToString(),
            afterFirst.Vehicle.Header.Status);
        Assert.Equal(
            SteelVehicleAcceptanceStatus.PartiallyIdentified,
            (await fixture.Context.Set<SteelVehicleAcceptance>()
                .AsNoTracking().SingleAsync(x => x.Id == completed.AcceptanceId)).Status);

        var remaining = afterFirst.Plates.Single(
            x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown));
        var duplicatePlanLine = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Acceptance.ResolveUnknownPlateAsync(
                remaining.Id,
                fixture.ResolveRequest(remaining, fixture.Lines[3]),
                fixture.ResolveImages(fixture.Lines[3]),
                fixture.Creator.Id));
        Assert.Equal(StatusCodes.Status409Conflict, duplicatePlanLine.StatusCode);

        var staleRequest = fixture.ResolveRequest(remaining, fixture.Lines[4]);
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [RII_STEEL_VEHICLE_ACCEPTED_PLATE] SET [UpdatedDate] = SYSUTCDATETIME() WHERE [Id] = {remaining.Id}");
        fixture.Context.ChangeTracker.Clear();
        var staleVersion = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Acceptance.ResolveUnknownPlateAsync(
                remaining.Id,
                staleRequest,
                fixture.ResolveImages(fixture.Lines[4]),
                fixture.Creator.Id));
        Assert.Equal(StatusCodes.Status409Conflict, staleVersion.StatusCode);

        var refreshed = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: true);
        remaining = refreshed!.Plates.Single(
            x => x.IdentityStatus == nameof(SteelPlateIdentityStatus.Unknown));
        var currentLine5 = await fixture.Context.Set<SteelReceiptPlanLine>()
            .AsNoTracking().SingleAsync(x => x.Id == fixture.Lines[4].Id);
        await fixture.Acceptance.ResolveUnknownPlateAsync(
            remaining.Id,
            fixture.ResolveRequest(remaining, currentLine5),
            fixture.ResolveImages(currentLine5),
            fixture.Creator.Id);

        var fullyResolved = await fixture.Acceptance.GetLatestByVehicleAsync(
            completed.Vehicle.Header.Id, canManageVehicleAcceptance: true);
        Assert.Equal(0, fullyResolved!.UnknownCount);
        Assert.False(fullyResolved.ContainsUnknownPlates);
        Assert.Equal(VehicleCheckInStatus.Completed.ToString(), fullyResolved.Vehicle.Header.Status);
        Assert.Equal(
            SteelVehicleAcceptanceStatus.Completed,
            (await fixture.Context.Set<SteelVehicleAcceptance>()
                .AsNoTracking().SingleAsync(x => x.Id == completed.AcceptanceId)).Status);

        var newlyResolvedLineIds = new[] { fixture.Lines[3].Id, fixture.Lines[4].Id };
        var converted = await fixture.SteelReceipt.ConvertAsync(
            fixture.Plan.Id,
            fixture.ConvertRequest(newlyResolvedLineIds),
            fixture.Creator.Id);
        Assert.Equal(2, converted.ConvertedLineCount);

        var replay = await fixture.Acceptance.CompleteAsync(
            request, [vehicleImage], [], fixture.Creator.Id);
        Assert.True(replay.Replayed);
        Assert.Equal(completed.AcceptanceId, replay.AcceptanceId);
        Assert.Equal(5, await fixture.Context.Set<SteelVehicleAcceptedPlate>()
            .CountAsync(x => x.VehicleAcceptanceId == completed.AcceptanceId));
    }

    [Fact]
    public async Task Legacy_acceptance_backfill_populates_query_result()
    {
        await using var fixture = await Fixture.CreateAsync();
        var vehicle = new VehicleCheckInHeader
        {
            BranchCode = fixture.Branch,
            PlateNo = "34 LEGACY 34",
            PlateNoNormalized = "34LEGACY34",
            SteelSheetCount = 1,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CheckedInAtUtc = DateTimeOffset.UtcNow,
            Status = VehicleCheckInStatus.Completed,
            CustomerId = fixture.Supplier.Id,
            CreatedBy = fixture.Creator.Id
        };
        fixture.Context.Add(vehicle);
        await fixture.Context.SaveChangesAsync();
        var acceptance = new SteelVehicleAcceptance
        {
            BranchCode = fixture.Branch,
            IdempotencyKey = Guid.NewGuid(),
            VehicleCheckInId = vehicle.Id,
            PlateCount = 1,
            TotalAcceptedQuantity = 1,
            Status = SteelVehicleAcceptanceStatus.Completed,
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            AcceptedBy = fixture.Creator.Id,
            CreatedBy = fixture.Creator.Id
        };
        fixture.Context.Add(acceptance);
        await fixture.Context.SaveChangesAsync();
        var line = fixture.Lines[0];
        line.VehicleAcceptanceId = acceptance.Id;
        line.ArrivalStatus = SteelArrivalStatus.Arrived;
        line.InspectionStatus = SteelInspectionStatus.Approved;
        line.ArrivedQuantity = line.ExpectedQuantity;
        line.ApprovedQuantity = line.ExpectedQuantity;
        await fixture.Context.SaveChangesAsync();

        await fixture.Context.Database.ExecuteSqlRawAsync(BackfillSql);
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Acceptance.GetLatestByVehicleAsync(
            vehicle.Id, canManageVehicleAcceptance: true);
        Assert.NotNull(result);
        var plate = Assert.Single(result.Plates);
        Assert.Equal(nameof(SteelPlateIdentityStatus.Known), plate.IdentityStatus);
        Assert.Equal(line.Id, plate.PlanLineId);

        await fixture.Context.Database.ExecuteSqlRawAsync(BackfillSql);
        Assert.Single(await fixture.Context.Set<SteelVehicleAcceptedPlate>()
            .Where(x => x.VehicleAcceptanceId == acceptance.Id).ToListAsync());
    }

    private const string BackfillSql =
        """
        ;WITH LegacyAcceptedLines AS
        (
            SELECT
                planLine.[Id] AS [PlanLineId],
                planLine.[VehicleAcceptanceId],
                acceptance.[VehicleCheckInId],
                acceptance.[BranchCode],
                CAST(ROW_NUMBER() OVER
                (
                    PARTITION BY planLine.[VehicleAcceptanceId]
                    ORDER BY planLine.[Id]
                ) AS int) AS [SequenceNo],
                COALESCE(acceptance.[CreatedBy], acceptance.[AcceptedBy]) AS [CreatedBy],
                COALESCE(
                    acceptance.[CreatedDate],
                    CONVERT(datetime2, acceptance.[AcceptedAtUtc])
                ) AS [CreatedDate]
            FROM [RII_STEEL_RECEIPT_PLAN_LINE] AS planLine
            INNER JOIN [RII_STEEL_VEHICLE_ACCEPTANCE] AS acceptance
                ON acceptance.[Id] = planLine.[VehicleAcceptanceId]
            WHERE planLine.[VehicleAcceptanceId] IS NOT NULL
              AND planLine.[IsDeleted] = CAST(0 AS bit)
              AND acceptance.[IsDeleted] = CAST(0 AS bit)
        )
        INSERT INTO [RII_STEEL_VEHICLE_ACCEPTED_PLATE]
        (
            [VehicleCheckInId],
            [VehicleAcceptanceId],
            [SequenceNo],
            [IdentityStatus],
            [PlanLineId],
            [ResolvedAtUtc],
            [ResolvedBy],
            [BranchCode],
            [CreatedDate],
            [UpdatedDate],
            [DeletedDate],
            [IsDeleted],
            [CreatedBy],
            [UpdatedBy],
            [DeletedBy]
        )
        SELECT
            legacy.[VehicleCheckInId],
            legacy.[VehicleAcceptanceId],
            legacy.[SequenceNo],
            N'Known',
            legacy.[PlanLineId],
            NULL,
            NULL,
            legacy.[BranchCode],
            legacy.[CreatedDate],
            NULL,
            NULL,
            CAST(0 AS bit),
            legacy.[CreatedBy],
            NULL,
            NULL
        FROM LegacyAcceptedLines AS legacy
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM [RII_STEEL_VEHICLE_ACCEPTED_PLATE] AS existing
            WHERE existing.[PlanLineId] = legacy.[PlanLineId]
              AND existing.[IsDeleted] = CAST(0 AS bit)
        );
        """;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly UnitOfWork _uow;
        private Fixture(WmsDbContext context, UnitOfWork uow, HttpContextAccessor http)
        {
            Context = context;
            _uow = uow;
            Http = http;
            var audit = new NullAuditWriter();
            var vehicleStorage = new MemoryStorage();
            var vehicleService = new VehicleCheckInService(
                uow, new FixedProjectSettings(), vehicleStorage, audit);
            Acceptance = new SteelVehicleAcceptanceService(
                uow,
                vehicleService,
                vehicleStorage,
                new MemoryStorage(),
                new GoodsReceiptPolicyService(uow, audit),
                audit);
            SteelReceipt = new SteelReceiptService(
                uow,
                new FakeGoodsReceiptOperations(context),
                new NullErpPostingCoordinator(),
                new UnusedStockMovementService(),
                audit,
                new MemoryStorage());
            Permissions = new PermissionAuthorizationService(uow);
        }

        public WmsDbContext Context { get; }
        public HttpContextAccessor Http { get; }
        public SteelVehicleAcceptanceService Acceptance { get; }
        public SteelReceiptService SteelReceipt { get; }
        public PermissionAuthorizationService Permissions { get; }
        public string Branch { get; } = $"UP{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        public User Creator { get; private set; } = null!;
        public User SameRole { get; private set; } = null!;
        public User Manager { get; private set; } = null!;
        public User NoPermission { get; private set; } = null!;
        public User WarehouseDeniedManager { get; private set; } = null!;
        public User Superadmin { get; private set; } = null!;
        public Customer Supplier { get; private set; } = null!;
        public Warehouse Warehouse { get; private set; } = null!;
        public Warehouse OtherWarehouse { get; private set; } = null!;
        public WarehouseLocation Location { get; private set; } = null!;
        public SteelReceiptPlan Plan { get; private set; } = null!;
        public List<SteelReceiptPlanLine> Lines { get; } = [];

        public static async Task<Fixture> CreateAsync()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<SteelVehicleUnknownPlateIntegrationTests>()
                .AddEnvironmentVariables()
                .Build();
            var template = configuration.GetConnectionString(
                "UnknownPlateIntegrationTestConnection");
            Assert.False(
                string.IsNullOrWhiteSpace(template),
                "Integration test için yalnızca test SQL Server'ını gösteren " +
                "ConnectionStrings:UnknownPlateIntegrationTestConnection gereklidir.");

            var builder = new SqlConnectionStringBuilder(template);
            Assert.Contains(
                "test",
                builder.InitialCatalog,
                StringComparison.OrdinalIgnoreCase);
            builder.InitialCatalog = $"verii_wms_unknown_plate_test_{Guid.NewGuid():N}";
            builder.TrustServerCertificate = true;
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlServer(builder.ConnectionString)
                .EnableSensitiveDataLogging(false)
                .Options;
            var context = new WmsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var http = new HttpContextAccessor
            {
                HttpContext = HttpContextFor(userId: 1, role: "superadmin", branch: null)
            };
            var uow = new UnitOfWork(context, http);
            var fixture = new Fixture(context, uow, http);
            try
            {
                await fixture.SeedAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        private async Task SeedAsync()
        {
            Creator = User("unknown-creator", "User");
            SameRole = User("unknown-peer", "User");
            Manager = User("unknown-manager", "Manager");
            NoPermission = User("unknown-no-permission", "Manager");
            WarehouseDeniedManager = User("unknown-denied-manager", "Manager");
            Superadmin = User("unknown-superadmin", "superadmin");
            Context.AddRange(
                Creator, SameRole, Manager, NoPermission,
                WarehouseDeniedManager, Superadmin);

            Supplier = new Customer
            {
                BranchCode = Branch,
                BusinessUnitCode = 1,
                CustomerCode = $"SUP-{Branch}",
                CustomerName = "Unknown plate test supplier"
            };
            Warehouse = new Warehouse
            {
                BranchCode = Branch,
                WarehouseCode = Random.Shared.Next(40_000, 49_000),
                WarehouseName = "Unknown plate test warehouse"
            };
            OtherWarehouse = new Warehouse
            {
                BranchCode = Branch,
                WarehouseCode = Random.Shared.Next(50_000, 59_000),
                WarehouseName = "Denied warehouse"
            };
            var stock = new Stock
            {
                BranchCode = Branch,
                BusinessUnitCode = 1,
                ErpStockCode = $"ST-{Branch}",
                StockName = "Steel sheet",
                BaseUnitCode = "ADET"
            };
            var series = new DocumentSeries
            {
                BranchCode = Branch,
                Code = $"GR-{Branch}",
                Name = "Unknown plate test receipt",
                DocumentType = WmsDocumentType.GoodsReceipt,
                Prefix = "UP",
                NumberLength = 8,
                StartNumber = 1,
                NextNumber = 1,
                IsActive = true
            };
            Context.AddRange(Supplier, Warehouse, OtherWarehouse, stock, series);
            await Context.SaveChangesAsync();

            Location = new WarehouseLocation
            {
                BranchCode = Branch,
                WarehouseId = Warehouse.Id,
                Code = $"REC-{Branch}",
                Name = "Receiving",
                LocationType = LocationTypes.Receiving,
                IsActive = true,
                IsPutaway = true
            };
            var otherLocation = new WarehouseLocation
            {
                BranchCode = Branch,
                WarehouseId = OtherWarehouse.Id,
                Code = $"OTH-{Branch}",
                Name = "Other",
                LocationType = LocationTypes.Receiving,
                IsActive = true,
                IsPutaway = true
            };
            Context.AddRange(Location, otherLocation);
            await Context.SaveChangesAsync();

            Plan = new SteelReceiptPlan
            {
                BranchCode = Branch,
                ImportReferenceNo = $"IMP-{Branch}",
                SourceFileName = "unknown-plate-test.xlsx",
                SupplierId = Supplier.Id,
                SupplierCodeSnapshot = Supplier.CustomerCode,
                SupplierNameSnapshot = Supplier.CustomerName,
                TargetWarehouseId = Warehouse.Id,
                ReceivingLocationId = Location.Id,
                DocumentSeriesId = series.Id,
                WaybillNo = "WB123",
                WaybillDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = SteelReceiptPlanStatus.Imported,
                TotalLineCount = 5,
                TotalExpectedQuantity = 5,
                ImportedAtUtc = DateTimeOffset.UtcNow,
                ImportedBy = Creator.Id
            };
            for (var index = 1; index <= 5; index++)
            {
                var line = new SteelReceiptPlanLine
                {
                    BranchCode = Branch,
                    LineNo = index,
                    DCode = $"D-{Branch}-{index}",
                    ExternalLineKey = $"{Branch}-{index}",
                    StockId = stock.Id,
                    StockCodeSnapshot = stock.ErpStockCode,
                    StockNameSnapshot = stock.StockName,
                    UnitCode = "ADET",
                    SupplierSerialNo = $"SER-{Branch}-{index}",
                    ExpectedQuantity = 1,
                    TargetWarehouseId = Warehouse.Id,
                    ReceivingLocationId = Location.Id
                };
                if (index <= 3)
                {
                    line.Attachments.Add(new SteelReceiptInspectionAttachment
                    {
                        BranchCode = Branch,
                        FileName = $"sheet-{index}.jpg",
                        ContentType = "image/jpeg",
                        StoragePath = $"memory/sheet-{index}.jpg",
                        FileSize = 1
                    });
                }
                Plan.Lines.Add(line);
                Lines.Add(line);
            }
            Context.Add(Plan);
            await Context.SaveChangesAsync();

            var legacyResolvePermission = new PermissionDefinition
            {
                BranchCode = "0",
                Code = ResolvePermission,
                Name = "Bilinmeyen SAC levhalarını eşleştir",
                IsActive = true,
                AvailableOnWeb = true
            };
            Context.Add(legacyResolvePermission);
            await Context.SaveChangesAsync();
            var vehicleManagePermission = await Context.Set<PermissionDefinition>()
                .SingleAsync(x => x.Code == VehicleManagePermission);
            var group = new PermissionGroup
            {
                BranchCode = "0",
                Name = $"Vehicle acceptance managers {Branch}",
                IsActive = true
            };
            group.GroupPermissions.Add(new PermissionGroupPermission
            {
                BranchCode = "0",
                PermissionDefinitionId = vehicleManagePermission.Id
            });
            group.GroupPermissions.Add(new PermissionGroupPermission
            {
                BranchCode = "0",
                PermissionDefinitionId = legacyResolvePermission.Id
            });
            Context.Add(group);
            await Context.SaveChangesAsync();
            foreach (var user in new[]
                     {
                         Creator, SameRole, Manager, WarehouseDeniedManager
                     })
            {
                Context.Add(new UserPermissionGroup
                {
                    BranchCode = "0",
                    UserId = user.Id,
                    PermissionGroupId = group.Id
                });
            }
            foreach (var user in new[] { Creator, SameRole, Manager, NoPermission })
            {
                Context.Add(new UserWarehouseAssignment
                {
                    BranchCode = Branch,
                    UserId = user.Id,
                    WarehouseId = Warehouse.Id
                });
            }
            Context.Add(new UserWarehouseAssignment
            {
                BranchCode = Branch,
                UserId = WarehouseDeniedManager.Id,
                WarehouseId = OtherWarehouse.Id
            });
            await Context.SaveChangesAsync();
            SetActor(Creator);
        }

        public CompleteSteelVehicleAcceptanceRequest CompleteRequest() => new(
            Guid.Parse("BDBA6334-5BB8-4D75-A430-3B35684AC99C"),
            new SaveVehicleCheckInRequest(
                null, null, Branch, "34 UNKNOWN 34", null, null, null, null, null,
                5, Supplier.Id, null),
            [
                .. Lines.Take(3).Select(line => new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    line.Id,
                    line.ReceivingLocationId,
                    Convert.ToBase64String(line.RowVersion),
                    null)),
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Unknown, null, null, null, null),
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Unknown, null, null, null, null)
            ],
            null);

        public ResolveUnknownPlateRequest ResolveRequest(
            AcceptedSteelPlateRow unknown,
            SteelReceiptPlanLine line) => new(
            line.Id,
            line.ReceivingLocationId,
            unknown.RowVersion,
            Convert.ToBase64String(line.RowVersion),
            null);

        public IReadOnlyList<SteelPlateImageUpload> ResolveImages(
            SteelReceiptPlanLine line) =>
            [new SteelPlateImageUpload(
                line.Id,
                new MemoryStream([4, 5, 6]),
                $"resolved-{line.Id}.jpg",
                "image/jpeg",
                3)];

        public ResolveUnknownPlateForm ResolveForm(ResolveUnknownPlateRequest request) => new()
        {
            RequestJson = JsonSerializer.Serialize(
                request,
                WmsJsonSerialization.ResponseOptions),
            PlateImages =
            [
                new FormFile(
                    new MemoryStream([4, 5, 6]),
                    0,
                    3,
                    "plateImages",
                    "resolved.jpg")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "image/jpeg"
                }
            ]
        };

        public ConvertSteelReceiptRequest ConvertRequest(IReadOnlyList<long> lineIds) => new(
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            lineIds,
            null,
            false,
            3,
            "Unknown plate integration conversion",
            SteelReceiptConversionMode.Direct,
            "WB123",
            null,
            DateOnly.FromDateTime(DateTime.UtcNow));

        public ClaimsPrincipal Principal(User user) => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtTokenIssuer.BranchCodeClaim, Branch)
            ],
            "test"));

        public SteelReceiptsController Controller(User user)
        {
            SetActor(user);
            return new SteelReceiptsController(SteelReceipt, Acceptance, Permissions)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = HttpContextFor(user.Id, user.Role, Branch)
                }
            };
        }

        private void SetActor(User user) =>
            Http.HttpContext = HttpContextFor(user.Id, user.Role, Branch);

        private static DefaultHttpContext HttpContextFor(
            long userId,
            string role,
            string? branch)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Role, role)
            };
            if (branch is not null)
                claims.Add(new Claim(JwtTokenIssuer.BranchCodeClaim, branch));
            return new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            };
        }

        private static User User(string name, string role) => new()
        {
            Username = $"{name}-{Guid.NewGuid():N}",
            Email = $"{name}-{Guid.NewGuid():N}@example.test",
            PasswordHash = "not-used",
            PasswordLength = 12,
            Role = role,
            IsActive = true
        };

        public async ValueTask DisposeAsync()
        {
            await _uow.DisposeAsync();
            await Context.Database.EnsureDeletedAsync();
            await Context.DisposeAsync();
        }
    }

    private sealed class NullAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            AuditLogWriteEntry entry,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedProjectSettings : IProjectSettingsService
    {
        private static readonly ProjectSettingsResponse Settings = new(
            1, "tr-TR", 2, "dd.MM.yyyy", "HH:mm", "yyyy", "UTC", true,
            6, 128, null, null, null, null);

        public Task<ProjectSettingsResponse> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task<ProjectSettingsResponse> UpdateAsync(
            UpdateProjectSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);
    }

    private sealed class MemoryStorage :
        IVehicleCheckInImageStorage,
        ISteelReceiptAttachmentStorage
    {
        public Task<string> SaveAsync(
            long headerId,
            VehicleImageUpload upload,
            CancellationToken ct = default) =>
            Task.FromResult($"memory/vehicle/{headerId}/{upload.FileName}");

        public Task<string> SaveAsync(
            long lineId,
            SteelReceiptAttachmentUpload upload,
            CancellationToken ct = default) =>
            Task.FromResult($"memory/steel/{lineId}/{upload.FileName}");

        public Task<Stream> OpenReadAsync(
            string storagePath,
            CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream([1]));

        public void Delete(string storagePath) { }
    }

    private sealed class FakeGoodsReceiptOperations(WmsDbContext context)
        : IGoodsReceiptOperationsService
    {
        public Task<GoodsReceiptQualityRequirementResult> ResolveQualityRequirementsAsync(
            ResolveGoodsReceiptQualityRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GoodsReceiptQualityRequirementResult(false, "None", []));

        public Task<ManualGoodsReceiptResult> CreateOrderlessTaskAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManualGoodsReceiptResult> CreateDirectReceiptAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            CancellationToken cancellationToken = default) =>
            CreateDirectReceiptDeferredErpAsync(
                request, actorUserId, true, cancellationToken);

        public async Task<ManualGoodsReceiptResult> CreateDirectReceiptDeferredErpAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            bool qualityAlreadyApproved,
            CancellationToken cancellationToken = default)
        {
            var header = new GoodsReceiptHeader
            {
                BranchCode = request.BranchCode,
                DocumentSeriesId = request.DocumentSeriesId,
                DocumentNo = $"UP-{Guid.NewGuid():N}"[..20],
                DocumentDate = request.DocumentDate,
                ReceiptType = GoodsReceiptType.Direct,
                InitiationMode = GoodsReceiptInitiationMode.DirectReceipt,
                ProcessType = GoodsReceiptProcessType.OrderlessDirectReceipt,
                LabelStrategy = request.LabelStrategy,
                CorrelationId = request.IdempotencyKey,
                SupplierId = request.SupplierId,
                TargetWarehouseId = request.TargetWarehouseId,
                ReceivingLocationId = request.ReceivingLocationId,
                Status = WarehouseOperationStatus.Completed,
                ApprovalStatus = OperationApprovalStatus.NotRequired,
                QualityStatus = OperationQualityStatus.Passed,
                PutawayStatus = OperationPutawayStatus.Pending,
                ErpIntegrationStatus = ErpIntegrationStatus.NotRequired,
                Priority = request.Priority,
                WaybillNo = request.WaybillNo,
                WaybillDate = request.WaybillDate,
                CreatedBy = actorUserId
            };
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var source = request.Lines[index];
                header.Lines.Add(new GoodsReceiptLine
                {
                    BranchCode = request.BranchCode,
                    LineNo = index + 1,
                    StockId = source.StockId,
                    StockCodeSnapshot = $"STOCK-{source.StockId}",
                    UnitCode = source.UnitCode ?? "ADET",
                    BaseUnitCode = source.UnitCode ?? "ADET",
                    ExpectedQuantity = source.Quantity,
                    ReceivedQuantity = source.Quantity,
                    AcceptedQuantity = source.Quantity,
                    TargetWarehouseId = source.TargetWarehouseId ?? request.TargetWarehouseId,
                    DefaultReceivingLocationId =
                        source.ReceivingLocationId ?? request.ReceivingLocationId,
                    Status = GoodsReceiptLineStatus.Received,
                    CreatedBy = actorUserId
                });
            }
            context.Add(header);
            await context.SaveChangesAsync(cancellationToken);
            return new ManualGoodsReceiptResult(
                header.Id,
                header.DocumentNo,
                header.InitiationMode,
                header.Status,
                null,
                null,
                null,
                null,
                null,
                header.Lines.Count,
                header.Lines.Sum(x => x.AcceptedQuantity),
                false,
                []);
        }

        public Task<PagedResponse<GoodsReceiptGridRow>> GetPagedAsync(
            PagedRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GoodsReceiptDetail> GetDetailAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullErpPostingCoordinator : IGoodsReceiptErpPostingCoordinator
    {
        public Task<ErpPostingResult?> PostIfEligibleAsync(
            long goodsReceiptId,
            long actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ErpPostingResult?>(null);
    }

    private sealed class UnusedStockMovementService : IStockMovementService
    {
        public Task<PagedResponse<StockMovementGridRow>> GetPagedAsync(
            PagedRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockMovementDetail> GetByIdAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockMovementPostResult> PostAsync(
            PostStockMovementRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockMovementPostResult> ReverseAsync(
            long operationId,
            ReverseStockMovementRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
