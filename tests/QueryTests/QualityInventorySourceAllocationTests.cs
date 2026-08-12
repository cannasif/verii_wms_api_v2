using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityInventorySourceAllocationTests
{
    [Fact]
    public async Task Quality_settings_persist_multiple_quarantine_warehouses_and_locations()
    {
        await using var db = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var warehouseOne = new verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse
            { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Depo 1" };
        var warehouseTwo = new verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse
            { BranchCode = "0", WarehouseCode = 2, WarehouseName = "Depo 2" };
        db.AddRange(warehouseOne, warehouseTwo);
        await db.SaveChangesAsync();
        var locationOne = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouseOne.Id, Code = "KAR-1", Name = "Karantina 1",
            LocationType = LocationTypes.Quarantine, IsQuarantine = true, IsActive = true, IsPickable = false
        };
        var locationTwo = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouseTwo.Id, Code = "KAR-2", Name = "Karantina 2",
            LocationType = LocationTypes.Quarantine, IsQuarantine = true, IsActive = true, IsPickable = false
        };
        var acceptedLocation = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouseOne.Id, Code = "ONAY-1", Name = "Onay 1",
            LocationType = LocationTypes.Cell, IsQuarantine = false, IsActive = true, IsPutaway = true
        };
        db.AddRange(locationOne, locationTwo, acceptedLocation);
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, HttpContext("0"));
        var service = new QualityService(unitOfWork, new RecordingAuditLogWriter(), null!, null!, null!, null!, null!);
        var result = await service.UpdateParametersAsync(new UpdateQualityParameterRequest(
            BranchCode: "0",
            AutoCreateInspectionOnReceipt: true,
            DefaultInspectionMode: QualityInspectionMode.InspectionRequired,
            DefaultFailAction: QualityFailAction.Quarantine,
            HoldInventoryUntilDecision: true,
            BlockPutawayUntilDecision: true,
            BlockErpPostingUntilDecision: true,
            RequireManagerApprovalForRelease: false,
            AllowPartialDecision: true,
            AllowDirectReceiptWhenNoRule: true,
            BlockReceiptWhenLotMissing: false,
            BlockReceiptWhenSerialMissing: false,
            BlockReceiptWhenExpiryMissing: false,
            DefaultQualityLocationId: null,
            DefaultAcceptedLocationId: acceptedLocation.Id,
            DefaultQuarantineLocationId: locationTwo.Id,
            DefaultRejectLocationId: null,
            QuarantineDestinations:
            [
                new(locationOne.Id, 100),
                new(locationTwo.Id, 200)
            ],
            WarehouseRoutes:
            [
                new(warehouseOne.Id, null, acceptedLocation.Id, locationOne.Id, locationOne.Id),
                new(warehouseTwo.Id, null, acceptedLocation.Id, locationTwo.Id, locationTwo.Id)
            ]), 42);

        Assert.Equal(locationTwo.Id, result.DefaultQuarantineLocationId);
        Assert.Equal(acceptedLocation.Id, result.DefaultAcceptedLocationId);
        Assert.Collection(result.QuarantineDestinations.OrderBy(destination => destination.Priority),
            first => Assert.Equal(locationOne.Id, first.LocationId),
            second =>
            {
                Assert.Equal(locationTwo.Id, second.LocationId);
                Assert.True(second.IsDefault);
            });
        Assert.Equal(2, await db.Set<QualityQuarantineDestination>().CountAsync());
        Assert.Collection(result.WarehouseRoutes.OrderBy(route => route.SourceWarehouseCode),
            first =>
            {
                Assert.Equal(warehouseOne.Id, first.SourceWarehouseId);
                Assert.Equal(locationOne.Id, first.QuarantineLocationId);
            },
            second =>
            {
                Assert.Equal(warehouseTwo.Id, second.SourceWarehouseId);
                Assert.Equal(locationTwo.Id, second.QuarantineLocationId);
            });
        Assert.Equal(2, await db.Set<QualityWarehouseRoute>().CountAsync());
    }

    [Fact]
    public async Task Failed_decision_is_written_to_audit_before_error_is_returned()
    {
        await using var db = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor());
        var audit = new RecordingAuditLogWriter();
        var service = new QualityService(unitOfWork, audit, null!, null!, null!, null!, null!);
        var request = new DecideQualityInspectionRequest(
            Guid.Empty,
            QualityDecision.Accepted,
            null,
            "test",
            null,
            null);

        await Assert.ThrowsAsync<AppException>(() =>
            service.DecideInspectionAsync(123, request, 7, true));

        var failure = Assert.Single(audit.Entries);
        Assert.Equal("quality.inspection.decide", failure.ActionType);
        Assert.Equal("123", failure.EntityId);
        Assert.Equal("Failed", failure.Result);
        Assert.False(string.IsNullOrWhiteSpace(failure.FailureReason));
    }

    [Fact]
    public void Allocation_prefers_exact_status_and_location_then_splits_remaining_quantity()
    {
        var candidates = new[]
        {
            Candidate(1, 10, "KABUL", "Available", 4, 1),
            Candidate(2, 20, "KALITE-2", "QualityHold", 5, 2),
            Candidate(3, 10, "KALITE-1", "QualityHold", 2, 3)
        };
        var remaining = candidates.ToDictionary(x => x.BalanceId, x => x.AvailableQuantity);

        var result = QualityService.AllocateInventorySources(
            candidates, remaining, 6, 10, "QualityHold", "STK-1", "GR-1", null, null);

        Assert.Collection(result,
            first =>
            {
                Assert.Equal(3, first.BalanceId);
                Assert.Equal(2, first.Quantity);
            },
            second =>
            {
                Assert.Equal(2, second.BalanceId);
                Assert.Equal(4, second.Quantity);
            });
        Assert.Equal(0, remaining[3]);
        Assert.Equal(1, remaining[2]);
        Assert.Equal(4, remaining[1]);
    }

    [Fact]
    public void Shared_balance_cannot_be_allocated_twice()
    {
        var candidates = new[] { Candidate(1, 10, "KALITE", "QualityHold", 5, 1) };
        var remaining = candidates.ToDictionary(x => x.BalanceId, x => x.AvailableQuantity);

        var first = QualityService.AllocateInventorySources(
            candidates, remaining, 3, 10, "QualityHold", "STK-1", "GR-1", null, null);
        var exception = Assert.Throws<AppException>(() => QualityService.AllocateInventorySources(
            candidates, remaining, 3, 10, "QualityHold", "STK-1", "GR-1", null, null));

        Assert.Single(first);
        Assert.Equal(2, remaining[1]);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Contains("Gereken: 3", exception.Message);
        Assert.Contains("kullanılabilir: 2", exception.Message);
    }

    [Fact]
    public void Insufficient_balance_error_contains_traceable_dimensions_and_locations()
    {
        var candidates = new[] { Candidate(7, 42, "KALITE-A", "QualityHold", 1.5m, 1) };
        var remaining = candidates.ToDictionary(x => x.BalanceId, x => x.AvailableQuantity);

        var exception = Assert.Throws<AppException>(() => QualityService.AllocateInventorySources(
            candidates, remaining, 2, 42, "QualityHold", "01/007", "GR1202600000084", "LOT-1", "SER-1"));

        Assert.Contains("01/007", exception.Message);
        Assert.Contains("GR1202600000084", exception.Message);
        Assert.Contains("LOT-1", exception.Message);
        Assert.Contains("SER-1", exception.Message);
        Assert.Contains("KALITE-A/QualityHold", exception.Message);
    }

    [Fact]
    public void Quarantine_destination_prefers_same_warehouse_before_default_and_priority()
    {
        var destinations = new[]
        {
            Destination(10, 1, 100, true),
            Destination(20, 2, 900, false),
            Destination(21, 2, 10, false)
        };

        var selected = QualityService.ResolveQuarantineDestination(destinations, null, 2);

        Assert.Equal(21, selected.LocationId);
    }

    [Fact]
    public void Explicit_quarantine_destination_is_used_when_it_is_configured()
    {
        var destinations = new[]
        {
            Destination(10, 1, 100, true),
            Destination(20, 2, 200, false)
        };

        var selected = QualityService.ResolveQuarantineDestination(destinations, 20, 1);

        Assert.Equal(20, selected.LocationId);
    }

    [Fact]
    public void Unconfigured_quarantine_destination_is_rejected()
    {
        var destinations = new[] { Destination(10, 1, 100, true) };

        var exception = Assert.Throws<AppException>(() =>
            QualityService.ResolveQuarantineDestination(destinations, 99, 1));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public void One_quality_line_can_be_split_into_three_decisions_with_independent_targets()
    {
        var line = InspectionLine(100, 3);
        var request = new[]
        {
            new QualityInspectionDispositionRequest(line.Id, QualityDecision.Accepted, 1, 11),
            new QualityInspectionDispositionRequest(line.Id, QualityDecision.Accepted, 1, 22),
            new QualityInspectionDispositionRequest(line.Id, QualityDecision.Rejected, 1, 33, "HASAR")
        };

        var parts = QualityService.BuildDecisionParts(
            [line], request, null, QualityDecision.Accepted);

        Assert.Collection(parts,
            first =>
            {
                Assert.Equal(QualityDecision.Accepted, first.Decision);
                Assert.Equal(1, first.Quantity);
                Assert.Equal(11, first.TargetLocationId);
            },
            second =>
            {
                Assert.Equal(QualityDecision.Accepted, second.Decision);
                Assert.Equal(1, second.Quantity);
                Assert.Equal(22, second.TargetLocationId);
            },
            third =>
            {
                Assert.Equal(QualityDecision.Rejected, third.Decision);
                Assert.Equal(1, third.Quantity);
                Assert.Equal(33, third.TargetLocationId);
                Assert.Equal("HASAR", third.ReasonCode);
            });
    }

    [Fact]
    public void Quality_distribution_total_must_equal_actionable_quantity()
    {
        var line = InspectionLine(100, 3);
        var request = new[]
        {
            new QualityInspectionDispositionRequest(line.Id, QualityDecision.Accepted, 1, 11),
            new QualityInspectionDispositionRequest(line.Id, QualityDecision.Rejected, 1, 33)
        };

        var exception = Assert.Throws<AppException>(() =>
            QualityService.BuildDecisionParts([line], request, null, QualityDecision.Accepted));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("3", exception.Message);
    }

    private static QualityService.QualityInventorySourceCandidate Candidate(
        long id,
        long locationId,
        string locationCode,
        string status,
        decimal quantity,
        int minutes) =>
        new(id, 1, locationId, locationCode, status, quantity, DateTime.UtcNow.AddMinutes(minutes));

    private static QualityQuarantineDestinationDto Destination(
        long locationId,
        long warehouseId,
        int priority,
        bool isDefault) =>
        new(locationId, locationId, warehouseId, checked((int)warehouseId), $"Depo {warehouseId}",
            $"KAR-{locationId}", $"Karantina {locationId}", priority, isDefault, true);

    private static QualityInspectionLine InspectionLine(long id, decimal quantity) => new()
    {
        Id = id,
        BranchCode = "0",
        StockId = 1,
        StockCodeSnapshot = "STK-1",
        Quantity = quantity,
        Decision = QualityDecision.Pending
    };

    private sealed class RecordingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteEntry> Entries { get; } = [];

        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static HttpContextAccessor HttpContext(string branchCode)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "42"),
                    new Claim(JwtTokenIssuer.BranchCodeClaim, branchCode)
                ],
                "Test"))
        };
        return new HttpContextAccessor { HttpContext = context };
    }
}
