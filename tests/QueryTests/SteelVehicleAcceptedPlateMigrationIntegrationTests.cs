using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelVehicleAcceptedPlateMigrationIntegrationTests
{
    private const string Baseline =
        "20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders";
    private const string Target =
        "20260801043004_AddSteelVehicleAcceptedPlate";
    private const string PermissionCode =
        "WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE";

    [Fact]
    public async Task Real_up_backfills_known_data_and_known_only_down_succeeds()
    {
        await using var fixture = await Fixture.CreateAtBaselineAsync();
        var legacy = await fixture.SeedLegacyKnownAcceptanceAsync();

        await fixture.Migrator.MigrateAsync(Target);
        fixture.Context.ChangeTracker.Clear();

        Assert.True(await fixture.TableExistsAsync(
            "RII_STEEL_VEHICLE_ACCEPTED_PLATE"));
        var plate = await fixture.Context.Set<SteelVehicleAcceptedPlate>()
            .SingleAsync();
        Assert.Equal(legacy.AcceptanceId, plate.VehicleAcceptanceId);
        Assert.Equal(legacy.PlanLineId, plate.PlanLineId);
        Assert.Equal(SteelPlateIdentityStatus.Known, plate.IdentityStatus);
        Assert.Equal(1, await fixture.Context.Set<PermissionDefinition>()
            .IgnoreQueryFilters()
            .CountAsync(x => x.Code == PermissionCode && !x.IsDeleted));
        var forwardConstraint = await fixture.ConstraintDefinitionAsync();
        Assert.Contains(">=", forwardConstraint);
        Assert.Contains("0", forwardConstraint);

        await fixture.Migrator.MigrateAsync(Baseline);

        Assert.False(await fixture.TableExistsAsync(
            "RII_STEEL_VEHICLE_ACCEPTED_PLATE"));
        Assert.False(await fixture.HasMigrationAsync(Target));
        Assert.DoesNotContain(">=", await fixture.ConstraintDefinitionAsync());
    }

    [Fact]
    public async Task Real_down_with_unknown_data_stops_before_deleting_objects()
    {
        await using var fixture = await Fixture.CreateAtBaselineAsync();
        var legacy = await fixture.SeedLegacyKnownAcceptanceAsync();
        await fixture.Migrator.MigrateAsync(Target);
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.Add(new SteelVehicleAcceptedPlate
        {
            BranchCode = fixture.Branch,
            VehicleCheckInId = legacy.VehicleId,
            VehicleAcceptanceId = legacy.AcceptanceId,
            SequenceNo = 2,
            IdentityStatus = SteelPlateIdentityStatus.Unknown,
            CreatedDate = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Migrator.MigrateAsync(Baseline));

        Assert.Contains("aktif bilinmeyen levhalar", error.ToString());
        Assert.True(await fixture.TableExistsAsync(
            "RII_STEEL_VEHICLE_ACCEPTED_PLATE"));
        Assert.True(await fixture.HasMigrationAsync(Target));
        Assert.True(await fixture.Context.Set<PermissionDefinition>()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Code == PermissionCode && !x.IsDeleted));
    }

    [Fact]
    public async Task Real_down_checks_soft_deleted_zero_quantity_rows_before_deleting_objects()
    {
        await using var fixture = await Fixture.CreateAtBaselineAsync();
        var legacy = await fixture.SeedLegacyKnownAcceptanceAsync();
        await fixture.Migrator.MigrateAsync(Target);
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.Add(new SteelVehicleAcceptance
        {
            BranchCode = fixture.Branch,
            IdempotencyKey = Guid.NewGuid(),
            VehicleCheckInId = legacy.VehicleId,
            PlateCount = 1,
            TotalAcceptedQuantity = 0,
            Status = SteelVehicleAcceptanceStatus.PartiallyIdentified,
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            AcceptedBy = 1,
            IsDeleted = true,
            DeletedDate = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Migrator.MigrateAsync(Baseline));

        Assert.Contains("sıfır kabul miktarlı", error.ToString());
        Assert.True(await fixture.TableExistsAsync(
            "RII_STEEL_VEHICLE_ACCEPTED_PLATE"));
        Assert.True(await fixture.HasMigrationAsync(Target));
        Assert.True(await fixture.Context.Set<PermissionDefinition>()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Code == PermissionCode && !x.IsDeleted));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(WmsDbContext context, string branch)
        {
            Context = context;
            Branch = branch;
            Migrator = context.GetService<IMigrator>();
        }

        public WmsDbContext Context { get; }
        public IMigrator Migrator { get; }
        public string Branch { get; }

        public static async Task<Fixture> CreateAtBaselineAsync()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<SteelVehicleAcceptedPlateMigrationIntegrationTests>()
                .AddEnvironmentVariables()
                .Build();
            var template = configuration.GetConnectionString(
                "UnknownPlateIntegrationTestConnection");
            Assert.False(
                string.IsNullOrWhiteSpace(template),
                "Gerçek migration testi için yalnızca izole test SQL Server'ını "
                + "gösteren ConnectionStrings:UnknownPlateIntegrationTestConnection gereklidir.");
            var builder = new SqlConnectionStringBuilder(template);
            Assert.Contains(
                "test",
                builder.InitialCatalog,
                StringComparison.OrdinalIgnoreCase);
            builder.InitialCatalog =
                $"verii_wms_unknown_plate_migration_test_{Guid.NewGuid():N}";
            builder.TrustServerCertificate = true;
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlServer(builder.ConnectionString)
                .EnableSensitiveDataLogging(false)
                .Options;
            var fixture = new Fixture(
                new WmsDbContext(options),
                $"MG{Guid.NewGuid():N}"[..10].ToUpperInvariant());
            try
            {
                await fixture.Context.Database.EnsureDeletedAsync();
                await fixture.Migrator.MigrateAsync(Baseline);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<LegacyIds> SeedLegacyKnownAcceptanceAsync()
        {
            var supplier = new Customer
            {
                BranchCode = Branch,
                BusinessUnitCode = 1,
                CustomerCode = $"SUP-{Branch}",
                CustomerName = "Migration test supplier"
            };
            var warehouse = new Warehouse
            {
                BranchCode = Branch,
                WarehouseCode = Random.Shared.Next(70_000, 79_000),
                WarehouseName = "Migration test warehouse"
            };
            var stock = new Stock
            {
                BranchCode = Branch,
                BusinessUnitCode = 1,
                ErpStockCode = $"ST-{Branch}",
                StockName = "Steel plate",
                BaseUnitCode = "ADET"
            };
            var series = new DocumentSeries
            {
                BranchCode = Branch,
                Code = $"GR-{Branch}",
                Name = "Migration test receipt",
                DocumentType = WmsDocumentType.GoodsReceipt,
                Prefix = "MG",
                NumberLength = 8,
                StartNumber = 1,
                NextNumber = 1,
                IsActive = true
            };
            Context.AddRange(supplier, warehouse, stock, series);
            await Context.SaveChangesAsync();
            var location = new WarehouseLocation
            {
                BranchCode = Branch,
                WarehouseId = warehouse.Id,
                Code = $"REC-{Branch}",
                Name = "Receiving",
                LocationType = LocationTypes.Receiving,
                IsActive = true,
                IsPutaway = true
            };
            var vehicle = new VehicleCheckInHeader
            {
                BranchCode = Branch,
                PlateNo = "34 MIGRATION 34",
                PlateNoNormalized = "34MIGRATION34",
                SteelSheetCount = 1,
                CustomerId = supplier.Id,
                CustomerCodeSnapshot = supplier.CustomerCode,
                CustomerNameSnapshot = supplier.CustomerName,
                CheckedInAtUtc = DateTimeOffset.UtcNow,
                BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = VehicleCheckInStatus.Completed
            };
            Context.AddRange(location, vehicle);
            await Context.SaveChangesAsync();
            var plan = new SteelReceiptPlan
            {
                BranchCode = Branch,
                ImportReferenceNo = $"IMP-{Branch}",
                SourceFileName = "migration-test.xlsx",
                SupplierId = supplier.Id,
                SupplierCodeSnapshot = supplier.CustomerCode,
                SupplierNameSnapshot = supplier.CustomerName,
                TargetWarehouseId = warehouse.Id,
                ReceivingLocationId = location.Id,
                DocumentSeriesId = series.Id,
                Status = SteelReceiptPlanStatus.Imported,
                TotalLineCount = 1,
                TotalExpectedQuantity = 1,
                ImportedAtUtc = DateTimeOffset.UtcNow,
                ImportedBy = 1
            };
            var acceptance = new SteelVehicleAcceptance
            {
                BranchCode = Branch,
                IdempotencyKey = Guid.NewGuid(),
                VehicleCheckInId = vehicle.Id,
                PlateCount = 1,
                TotalAcceptedQuantity = 1,
                Status = SteelVehicleAcceptanceStatus.Completed,
                AcceptedAtUtc = DateTimeOffset.UtcNow,
                AcceptedBy = 1,
                CreatedBy = 1,
                CreatedDate = DateTime.UtcNow
            };
            Context.AddRange(plan, acceptance);
            await Context.SaveChangesAsync();
            var line = new SteelReceiptPlanLine
            {
                BranchCode = Branch,
                PlanId = plan.Id,
                LineNo = 1,
                DCode = $"D-{Branch}-1",
                ExternalLineKey = $"{Branch}-1",
                StockId = stock.Id,
                StockCodeSnapshot = stock.ErpStockCode,
                StockNameSnapshot = stock.StockName,
                UnitCode = "ADET",
                SupplierSerialNo = $"SER-{Branch}-1",
                ExpectedQuantity = 1,
                ArrivedQuantity = 1,
                ApprovedQuantity = 1,
                TargetWarehouseId = warehouse.Id,
                ReceivingLocationId = location.Id,
                ArrivalStatus = SteelArrivalStatus.Arrived,
                InspectionStatus = SteelInspectionStatus.Approved,
                ConversionStatus = SteelReceiptConversionStatus.NotCreated,
                VehicleAcceptanceId = acceptance.Id
            };
            Context.Add(line);
            await Context.SaveChangesAsync();
            return new(vehicle.Id, acceptance.Id, line.Id);
        }

        public async Task<bool> TableExistsAsync(string tableName) =>
            await Context.Database.SqlQueryRaw<int>(
                    """
                    SELECT CASE WHEN OBJECT_ID({0}, N'U') IS NULL THEN 0 ELSE 1 END AS [Value]
                    """,
                    tableName)
                .SingleAsync() == 1;

        public async Task<bool> HasMigrationAsync(string migrationId) =>
            await Context.Database.SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*) AS [Value]
                    FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = {0}
                    """,
                    migrationId)
                .SingleAsync() == 1;

        public Task<string> ConstraintDefinitionAsync() =>
            Context.Database.SqlQueryRaw<string>(
                    """
                    SELECT [definition] AS [Value]
                    FROM sys.check_constraints
                    WHERE [name] = N'CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY'
                    """)
                .SingleAsync();

        public async ValueTask DisposeAsync()
        {
            try { await Context.Database.EnsureDeletedAsync(); }
            finally { await Context.DisposeAsync(); }
        }
    }

    private sealed record LegacyIds(
        long VehicleId,
        long AcceptanceId,
        long PlanLineId);
}
