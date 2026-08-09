using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.ProjectSettings.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class VehicleCheckInRepeatArrivalTests
{
    [Fact]
    public void Plate_history_index_is_not_unique()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"vehicle-index-{Guid.NewGuid():N}")
            .Options;
        using var context = new WmsDbContext(options);

        var index = context.Model
            .FindEntityType(typeof(VehicleCheckInHeader))!
            .GetIndexes()
            .Single(x => x.GetDatabaseName() ==
                "IX_RII_VEHICLE_CHECKIN_HEADER_PlateHistory");

        Assert.False(index.IsUnique);
    }

    [Fact]
    public async Task Same_plate_can_create_multiple_arrivals_on_the_same_business_day()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"vehicle-repeat-arrival-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new WmsDbContext(options);
        await using var uow = new UnitOfWork(context, new HttpContextAccessor());
        var service = new VehicleCheckInService(
            uow,
            new FixedProjectSettings(),
            new NoOpImageStorage(),
            new NoOpAuditWriter());

        var first = await service.SaveAsync(Request("34 ABC 123", "İlk geliş"), actor: 11);
        var second = await service.SaveAsync(Request("34ABC123", "İkinci geliş"), actor: 12);

        Assert.NotEqual(first.Header.Id, second.Header.Id);
        Assert.Equal(first.Header.BusinessDate, second.Header.BusinessDate);
        Assert.Equal(
            2,
            await context.Set<VehicleCheckInHeader>()
                .CountAsync(x =>
                    x.BranchCode == "0" &&
                    x.PlateNoNormalized == "34ABC123" &&
                    x.BusinessDate == first.Header.BusinessDate));

        var latest = await service.FindTodayByPlateAsync("0", "34 ABC 123");

        Assert.NotNull(latest);
        Assert.Equal(second.Header.Id, latest.Header.Id);
        Assert.Equal("İkinci geliş", latest.Header.Note);
    }

    private static SaveVehicleCheckInRequest Request(string plateNo, string note) =>
        new(
            Id: null,
            RowVersion: null,
            BranchCode: "0",
            PlateNo: plateNo,
            TrailerPlateNo: null,
            DriverFirstName: "Test",
            DriverLastName: "Sürücü",
            DriverPhone: null,
            CarrierName: "Test Nakliye",
            SteelSheetCount: 1,
            CustomerId: null,
            Note: note);

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

    private sealed class NoOpAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            AuditLogWriteEntry entry,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpImageStorage : IVehicleCheckInImageStorage
    {
        public Task<string> SaveAsync(
            long headerId,
            VehicleImageUpload upload,
            CancellationToken ct = default) =>
            Task.FromResult($"memory/vehicle/{headerId}/{upload.FileName}");

        public Task<Stream> OpenReadAsync(
            string storagePath,
            CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public void Delete(string storagePath)
        {
        }
    }
}
