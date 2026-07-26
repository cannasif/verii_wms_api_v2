using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Modules.AccessControl.Infrastructure;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.Audit.Infrastructure;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.BarcodeDesigner.Infrastructure;
using verii_wms_api_v2.Modules.Customer.Infrastructure;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Infrastructure;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Location.Infrastructure;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Modules.Packing.Infrastructure;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Modules.ProjectSettings.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Quality.Infrastructure;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Infrastructure;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.Shipping.Infrastructure;
using verii_wms_api_v2.Modules.Smtp.Domain;
using verii_wms_api_v2.Modules.Smtp.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Infrastructure;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.StockTracking.Infrastructure;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockMovement.Infrastructure;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockBalance.Infrastructure;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Infrastructure;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseInbound.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Infrastructure;
using verii_wms_api_v2.Modules.SystemManagement.Domain;
using verii_wms_api_v2.Modules.SystemManagement.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Infrastructure;
using verii_wms_api_v2.Modules.YapCode.Infrastructure;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;
using DocumentSeriesEntity = verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries;

namespace verii_wms_api_v2.Modules.Identity.Infrastructure;

public sealed class WmsDbContext(DbContextOptions<WmsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserDetail> UserDetails => Set<UserDetail>();
    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<StockEntity> Stocks => Set<StockEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<WarehouseEntity> Warehouses => Set<WarehouseEntity>();
    public DbSet<YapCodeEntity> YapCodes => Set<YapCodeEntity>();
    public DbSet<WarehouseLocation> Locations => Set<WarehouseLocation>();
    public DbSet<PackagingMaterial> PackagingMaterials => Set<PackagingMaterial>();
    public DbSet<PackingStation> PackingStations => Set<PackingStation>();
    public DbSet<PackingPolicy> PackingPolicies => Set<PackingPolicy>();
    public DbSet<PackagingSpecification> PackagingSpecifications => Set<PackagingSpecification>();
    public DbSet<PackingSession> PackingSessions => Set<PackingSession>();
    public DbSet<HandlingUnit> HandlingUnits => Set<HandlingUnit>();
    public DbSet<HandlingUnitLine> HandlingUnitLines => Set<HandlingUnitLine>();
    public DbSet<PackingEvent> PackingEvents => Set<PackingEvent>();
    public DbSet<PackingPrintJob> PackingPrintJobs => Set<PackingPrintJob>();
    public DbSet<PackingScaleReading> PackingScaleReadings => Set<PackingScaleReading>();
    public DbSet<DocumentSeriesEntity> DocumentSeries => Set<DocumentSeriesEntity>();
    public DbSet<GoodsReceiptHeader> GoodsReceiptHeaders => Set<GoodsReceiptHeader>();
    public DbSet<GoodsReceiptSourceDocument> GoodsReceiptSourceDocuments => Set<GoodsReceiptSourceDocument>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<GoodsReceiptLineSource> GoodsReceiptLineSources => Set<GoodsReceiptLineSource>();
    public DbSet<GoodsReceiptStatusHistory> GoodsReceiptStatusHistory => Set<GoodsReceiptStatusHistory>();
    public DbSet<GoodsReceiptTask> GoodsReceiptTasks => Set<GoodsReceiptTask>();
    public DbSet<GoodsReceiptTaskLine> GoodsReceiptTaskLines => Set<GoodsReceiptTaskLine>();
    public DbSet<GoodsReceiptTaskLineTracking> GoodsReceiptTaskLineTrackings => Set<GoodsReceiptTaskLineTracking>();
    public DbSet<GoodsReceiptTaskAssignment> GoodsReceiptTaskAssignments => Set<GoodsReceiptTaskAssignment>();
    public DbSet<GoodsReceiptLabelBatch> GoodsReceiptLabelBatches => Set<GoodsReceiptLabelBatch>();
    public DbSet<GoodsReceiptLabel> GoodsReceiptLabels => Set<GoodsReceiptLabel>();
    public DbSet<GoodsReceiptPolicy> GoodsReceiptPolicies => Set<GoodsReceiptPolicy>();
    public DbSet<GoodsReceiptExecution> GoodsReceiptExecutions => Set<GoodsReceiptExecution>();
    public DbSet<GoodsReceiptExecutionLine> GoodsReceiptExecutionLines => Set<GoodsReceiptExecutionLine>();
    public DbSet<GoodsReceiptRoutingBatch> GoodsReceiptRoutingBatches => Set<GoodsReceiptRoutingBatch>();
    public DbSet<GoodsReceiptRoutingAllocation> GoodsReceiptRoutingAllocations => Set<GoodsReceiptRoutingAllocation>();
    public DbSet<SteelReceiptPlan> SteelReceiptPlans => Set<SteelReceiptPlan>();
    public DbSet<SteelReceiptPlanLine> SteelReceiptPlanLines => Set<SteelReceiptPlanLine>();
    public DbSet<SteelReceiptInspectionAttachment> SteelReceiptInspectionAttachments => Set<SteelReceiptInspectionAttachment>();
    public DbSet<SteelReceiptPlacement> SteelReceiptPlacements => Set<SteelReceiptPlacement>();
    public DbSet<VehicleCheckInHeader> VehicleCheckInHeaders => Set<VehicleCheckInHeader>();
    public DbSet<VehicleCheckInImage> VehicleCheckInImages => Set<VehicleCheckInImage>();
    public DbSet<QualityParameter> QualityParameters => Set<QualityParameter>();
    public DbSet<QualityRule> QualityRules => Set<QualityRule>();
    public DbSet<QualityInspection> QualityInspections => Set<QualityInspection>();
    public DbSet<QualityInspectionLine> QualityInspectionLines => Set<QualityInspectionLine>();
    public DbSet<SerialNumberRule> SerialNumberRules => Set<SerialNumberRule>();
    public DbSet<StockSerialRegistry> StockSerialRegistry => Set<StockSerialRegistry>();
    public DbSet<StockTrackingPolicy> StockTrackingPolicies => Set<StockTrackingPolicy>();
    public DbSet<BarcodeTemplate> BarcodeTemplates => Set<BarcodeTemplate>();
    public DbSet<BarcodeTemplateVersion> BarcodeTemplateVersions => Set<BarcodeTemplateVersion>();
    public DbSet<BarcodePolicy> BarcodePolicies => Set<BarcodePolicy>();
    public DbSet<BarcodePolicyProfile> BarcodePolicyProfiles => Set<BarcodePolicyProfile>();
    public DbSet<BarcodePolicyProfileSegment> BarcodePolicyProfileSegments => Set<BarcodePolicyProfileSegment>();
    public DbSet<GeneratedBarcode> GeneratedBarcodes => Set<GeneratedBarcode>();
    public DbSet<StockMovementOperation> StockMovementOperations => Set<StockMovementOperation>();
    public DbSet<StockMovementEntry> StockMovementEntries => Set<StockMovementEntry>();
    public DbSet<LocationStockBalance> LocationStockBalances => Set<LocationStockBalance>();
    public DbSet<WarehouseStockBalance> WarehouseStockBalances => Set<WarehouseStockBalance>();
    public DbSet<StockBalanceProjectionState> StockBalanceProjectionStates => Set<StockBalanceProjectionState>();
    public DbSet<StockReservationOperation> StockReservationOperations => Set<StockReservationOperation>();
    public DbSet<StockReservationEntry> StockReservationEntries => Set<StockReservationEntry>();
    public DbSet<PermissionDefinition> PermissionDefinitions => Set<PermissionDefinition>();
    public DbSet<PermissionGroup> PermissionGroups => Set<PermissionGroup>();
    public DbSet<PermissionGroupPermission> PermissionGroupPermissions => Set<PermissionGroupPermission>();
    public DbSet<UserPermissionGroup> UserPermissionGroups => Set<UserPermissionGroup>();
    public DbSet<SmtpSetting> SmtpSettings => Set<SmtpSetting>();
    public DbSet<HangfireExecutionLog> HangfireExecutionLogs => Set<HangfireExecutionLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProjectSetting> ProjectSettings => Set<ProjectSetting>();
    public DbSet<ErpPostingRecord> ErpPostingRecords => Set<ErpPostingRecord>();
    public DbSet<ErpIntegrationAttempt> ErpIntegrationAttempts => Set<ErpIntegrationAttempt>();
    public DbSet<ErpCancellationRecord> ErpCancellationRecords => Set<ErpCancellationRecord>();
    public DbSet<ErpCancellationAttempt> ErpCancellationAttempts => Set<ErpCancellationAttempt>();
    public DbSet<WarehouseTransferHeader> WarehouseTransferHeaders => Set<WarehouseTransferHeader>();
    public DbSet<WarehouseTransferSourceDocument> WarehouseTransferSourceDocuments => Set<WarehouseTransferSourceDocument>();
    public DbSet<WarehouseTransferLine> WarehouseTransferLines => Set<WarehouseTransferLine>();
    public DbSet<WarehouseTransferLineSource> WarehouseTransferLineSources => Set<WarehouseTransferLineSource>();
    public DbSet<WarehouseTransferTracking> WarehouseTransferTrackings => Set<WarehouseTransferTracking>();
    public DbSet<WarehouseTransferTask> WarehouseTransferTasks => Set<WarehouseTransferTask>();
    public DbSet<WarehouseTransferTaskLine> WarehouseTransferTaskLines => Set<WarehouseTransferTaskLine>();
    public DbSet<WarehouseTransferTaskAssignment> WarehouseTransferTaskAssignments => Set<WarehouseTransferTaskAssignment>();
    public DbSet<WarehouseTransferStatusHistory> WarehouseTransferStatusHistory => Set<WarehouseTransferStatusHistory>();
    public DbSet<WarehouseTransferPolicy> WarehouseTransferPolicies => Set<WarehouseTransferPolicy>();
    public DbSet<WarehouseInboundHeader> WarehouseInboundHeaders => Set<WarehouseInboundHeader>();
    public DbSet<WarehouseInboundSourceDocument> WarehouseInboundSourceDocuments => Set<WarehouseInboundSourceDocument>();
    public DbSet<WarehouseInboundLine> WarehouseInboundLines => Set<WarehouseInboundLine>();
    public DbSet<WarehouseInboundLineSource> WarehouseInboundLineSources => Set<WarehouseInboundLineSource>();
    public DbSet<WarehouseInboundStatusHistory> WarehouseInboundStatusHistory => Set<WarehouseInboundStatusHistory>();
    public DbSet<WarehouseInboundTask> WarehouseInboundTasks => Set<WarehouseInboundTask>();
    public DbSet<WarehouseInboundTaskLine> WarehouseInboundTaskLines => Set<WarehouseInboundTaskLine>();
    public DbSet<WarehouseInboundTaskLineTracking> WarehouseInboundTaskLineTrackings => Set<WarehouseInboundTaskLineTracking>();
    public DbSet<WarehouseInboundTaskAssignment> WarehouseInboundTaskAssignments => Set<WarehouseInboundTaskAssignment>();
    public DbSet<WarehouseInboundLabelBatch> WarehouseInboundLabelBatches => Set<WarehouseInboundLabelBatch>();
    public DbSet<WarehouseInboundLabel> WarehouseInboundLabels => Set<WarehouseInboundLabel>();
    public DbSet<WarehouseInboundPolicy> WarehouseInboundPolicies => Set<WarehouseInboundPolicy>();
    public DbSet<WarehouseInboundExecution> WarehouseInboundExecutions => Set<WarehouseInboundExecution>();
    public DbSet<WarehouseInboundExecutionLine> WarehouseInboundExecutionLines => Set<WarehouseInboundExecutionLine>();
    public DbSet<WarehouseOutboundHeader> WarehouseOutboundHeaders => Set<WarehouseOutboundHeader>();
    public DbSet<WarehouseOutboundSourceDocument> WarehouseOutboundSourceDocuments => Set<WarehouseOutboundSourceDocument>();
    public DbSet<WarehouseOutboundLine> WarehouseOutboundLines => Set<WarehouseOutboundLine>();
    public DbSet<WarehouseOutboundLineSource> WarehouseOutboundLineSources => Set<WarehouseOutboundLineSource>();
    public DbSet<WarehouseOutboundTracking> WarehouseOutboundTrackings => Set<WarehouseOutboundTracking>();
    public DbSet<WarehouseOutboundTask> WarehouseOutboundTasks => Set<WarehouseOutboundTask>();
    public DbSet<WarehouseOutboundTaskLine> WarehouseOutboundTaskLines => Set<WarehouseOutboundTaskLine>();
    public DbSet<WarehouseOutboundTaskAssignment> WarehouseOutboundTaskAssignments => Set<WarehouseOutboundTaskAssignment>();
    public DbSet<WarehouseOutboundStatusHistory> WarehouseOutboundStatusHistory => Set<WarehouseOutboundStatusHistory>();
    public DbSet<WarehouseOutboundPolicy> WarehouseOutboundPolicies => Set<WarehouseOutboundPolicy>();    public DbSet<ShipmentHeader> ShipmentHeaders => Set<ShipmentHeader>();
    public DbSet<ShipmentSourceDocument> ShipmentSourceDocuments => Set<ShipmentSourceDocument>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<ShipmentLineSource> ShipmentLineSources => Set<ShipmentLineSource>();
    public DbSet<ShipmentTracking> ShipmentTrackings => Set<ShipmentTracking>();
    public DbSet<ShipmentTask> ShipmentTasks => Set<ShipmentTask>();
    public DbSet<ShipmentTaskLine> ShipmentTaskLines => Set<ShipmentTaskLine>();
    public DbSet<ShipmentTaskAssignment> ShipmentTaskAssignments => Set<ShipmentTaskAssignment>();
    public DbSet<ShipmentStatusHistory> ShipmentStatusHistory => Set<ShipmentStatusHistory>();
    public DbSet<ShipmentPolicy> ShipmentPolicies => Set<ShipmentPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("RII_USERS"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Username).HasMaxLength(100).IsRequired(); entity.Property(x => x.Email).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired(); entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TokenVersion).HasDefaultValue(1).IsRequired(); entity.HasIndex(x => x.Username).IsUnique(); entity.HasIndex(x => x.Email).IsUnique();
            entity.HasData(new User { Id = 1, Username = "admin", Email = "admin@v3rii.com", PasswordHash = "$2a$11$/miyTaLTVkU0keOJabjkQ.bKF4Rb6a2jhuLWDz67I4LLxjwWQ6IJW", Role = "superadmin", IsActive = true });
        });
        modelBuilder.Entity<UserDetail>(entity =>
        {
            entity.ToTable("RII_USER_DETAILS"); entity.HasKey(x => x.UserId); entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired(); entity.Property(x => x.LastName).HasMaxLength(100).IsRequired(); entity.Property(x => x.Phone).HasMaxLength(40); entity.Property(x => x.ProfilePictureUrl).HasMaxLength(500); entity.Property(x => x.Description).HasMaxLength(2000); entity.Property(x => x.Height).HasColumnType("decimal(6,2)"); entity.Property(x => x.Weight).HasColumnType("decimal(6,2)");
            entity.HasOne(x => x.User).WithOne(x => x.Detail).HasForeignKey<UserDetail>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasData(new UserDetail { UserId = 1, FirstName = "System", LastName = "Administrator" });
        });

        modelBuilder.ApplyConfiguration(new StockConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenSessionConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new YapCodeConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseLocationConfiguration());
        modelBuilder.ApplyConfiguration(new PackagingMaterialConfiguration());
        modelBuilder.ApplyConfiguration(new PackingStationConfiguration());
        modelBuilder.ApplyConfiguration(new PackingPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new PackagingSpecificationConfiguration());
        modelBuilder.ApplyConfiguration(new PackingSessionConfiguration());
        modelBuilder.ApplyConfiguration(new HandlingUnitConfiguration());
        modelBuilder.ApplyConfiguration(new HandlingUnitLineConfiguration());
        modelBuilder.ApplyConfiguration(new PackingEventConfiguration());
        modelBuilder.ApplyConfiguration(new PackingPrintJobConfiguration());
        modelBuilder.ApplyConfiguration(new PackingScaleReadingConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentSeriesConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptHeaderConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptSourceDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptLineConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptLineSourceConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptTaskConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptTaskLineConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptTaskLineTrackingConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptTaskAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptLabelBatchConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptLabelConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptExecutionLineConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptRoutingBatchConfiguration());
        modelBuilder.ApplyConfiguration(new GoodsReceiptRoutingAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new SteelReceiptPlanConfiguration());
        modelBuilder.ApplyConfiguration(new SteelReceiptPlanLineConfiguration());
        modelBuilder.ApplyConfiguration(new SteelReceiptInspectionAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new SteelReceiptPlacementConfiguration());
        modelBuilder.ApplyConfiguration(new SteelVehicleAcceptanceConfiguration());
        modelBuilder.ApplyConfiguration(new VehicleCheckInHeaderConfiguration());
        modelBuilder.ApplyConfiguration(new VehicleCheckInImageConfiguration());
        modelBuilder.ApplyConfiguration(new QualityParameterConfiguration());
        modelBuilder.ApplyConfiguration(new QualityRuleConfiguration());
        modelBuilder.ApplyConfiguration(new QualityInspectionConfiguration());
        modelBuilder.ApplyConfiguration(new QualityInspectionLineConfiguration());
        modelBuilder.ApplyConfiguration(new SerialNumberRuleConfiguration());
        modelBuilder.ApplyConfiguration(new StockSerialRegistryConfiguration());
        modelBuilder.ApplyConfiguration(new StockTrackingPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new BarcodeTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new BarcodeTemplateVersionConfiguration());
        modelBuilder.ApplyConfiguration(new BarcodePolicyConfiguration());
        modelBuilder.ApplyConfiguration(new BarcodePolicyProfileConfiguration());
        modelBuilder.ApplyConfiguration(new BarcodePolicyProfileSegmentConfiguration());
        modelBuilder.ApplyConfiguration(new GeneratedBarcodeConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementOperationConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementEntryConfiguration());
        modelBuilder.ApplyConfiguration(new LocationStockBalanceConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseStockBalanceConfiguration());
        modelBuilder.ApplyConfiguration(new StockBalanceProjectionStateConfiguration());
        modelBuilder.ApplyConfiguration(new StockReservationOperationConfiguration());
        modelBuilder.ApplyConfiguration(new StockReservationEntryConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionGroupConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionGroupPermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserPermissionGroupConfiguration());
        modelBuilder.ApplyConfiguration(new SmtpSettingConfiguration());
        modelBuilder.ApplyConfiguration(new HangfireExecutionLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectSettingConfiguration());
        modelBuilder.ApplyConfiguration(new ErpPostingRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ErpIntegrationAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new ErpCancellationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ErpCancellationAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferHeaderConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferSourceDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferLineSourceConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferTrackingConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferTaskConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferTaskLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferTaskAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseTransferPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundHeaderConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundSourceDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundLineSourceConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundTaskConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundTaskLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundTaskLineTrackingConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundTaskAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundLabelBatchConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundLabelConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseInboundExecutionLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundHeaderConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundSourceDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundLineSourceConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundTrackingConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundTaskConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundTaskLineConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundTaskAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseOutboundPolicyConfiguration());        modelBuilder.ApplyConfiguration(new ShipmentHeaderConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentSourceDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentLineConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentLineSourceConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentTrackingConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentTaskConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentTaskLineConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentTaskAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentPolicyConfiguration());

        var seedDate = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1001, BranchCode="0", Code="SYSTEM.USERS.VIEW", Name="Kullanıcıları Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1002, BranchCode="0", Code="SYSTEM.USERS.MANAGE", Name="Kullanıcıları Yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1003, BranchCode="0", Code="SYSTEM.PERMISSIONS.VIEW", Name="İzinleri Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1004, BranchCode="0", Code="SYSTEM.PERMISSIONS.MANAGE", Name="İzinleri Yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1005, BranchCode="0", Code="SYSTEM.SMTP.MANAGE", Name="SMTP Ayarlarını Yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1006, BranchCode="0", Code="SYSTEM.HANGFIRE.VIEW", Name="Hangfire İzle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1007, BranchCode="0", Code="SYSTEM.HANGFIRE.TRIGGER", Name="Hangfire Job Tetikle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1008, BranchCode="0", Code="SYSTEM.AUDIT.VIEW", Name="Audit Kayıtlarını Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1009, BranchCode="0", Code="WMS.LOCATIONS.VIEW", Name="Raf Tanımlarını Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1010, BranchCode="0", Code="WMS.LOCATIONS.CREATE", Name="Raf Tanımı Oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1011, BranchCode="0", Code="WMS.LOCATIONS.UPDATE", Name="Raf Tanımını Güncelle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1012, BranchCode="0", Code="WMS.LOCATIONS.DELETE", Name="Raf Tanımını Sil", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1013, BranchCode="0", Code="WMS.STOCK_MOVEMENTS.VIEW", Name="Stok Hareketlerini Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1014, BranchCode="0", Code="WMS.STOCK_MOVEMENTS.POST", Name="Stok Hareketi Kaydet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1015, BranchCode="0", Code="WMS.STOCK_MOVEMENTS.REVERSE", Name="Stok Hareketini Ters Çevir", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1016, BranchCode="0", Code="WMS.STOCK_BALANCES.VIEW", Name="Stok Bakiyelerini Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1017, BranchCode="0", Code="WMS.STOCK_BALANCES.RECONCILE", Name="Stok Bakiyelerini Uzlaştır ve Onar", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1018, BranchCode="0", Code="SYSTEM.PROJECT_SETTINGS.VIEW", Name="Proje Ayarlarını Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1019, BranchCode="0", Code="SYSTEM.PROJECT_SETTINGS.MANAGE", Name="Proje Ayarlarını Yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1020, BranchCode="0", Code="WMS.DOCUMENT_SERIES.VIEW", Name="Belge Serilerini Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1021, BranchCode="0", Code="WMS.DOCUMENT_SERIES.CREATE", Name="Belge Serisi Oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1022, BranchCode="0", Code="WMS.DOCUMENT_SERIES.UPDATE", Name="Belge Serisini Güncelle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1023, BranchCode="0", Code="WMS.DOCUMENT_SERIES.DELETE", Name="Belge Serisini Sil", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1024, BranchCode="0", Code="WMS.BARCODE_DESIGNER.VIEW", Name="Barkod Şablonlarını Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1025, BranchCode="0", Code="WMS.BARCODE_DESIGNER.CREATE", Name="Barkod Şablonu Oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1026, BranchCode="0", Code="WMS.BARCODE_DESIGNER.UPDATE", Name="Barkod Şablonunu Güncelle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1027, BranchCode="0", Code="WMS.BARCODE_DESIGNER.DELETE", Name="Barkod Şablonunu Sil", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1028, BranchCode="0", Code="WMS.BARCODE_DESIGNER.PUBLISH", Name="Barkod Şablonu Yayınla", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1029, BranchCode="0", Code="WMS.BARCODE_DESIGNER.PRINT", Name="Barkod Etiketi Yazdır", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1030, BranchCode="0", Code="WMS.BARCODE_POLICY.VIEW", Name="Genel Barkod Politikasını Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1031, BranchCode="0", Code="WMS.BARCODE_POLICY.MANAGE", Name="Genel Barkod Politikasını Yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1032, BranchCode="0", Code="WMS.BARCODE_POLICY.GENERATE", Name="Politikaya Göre Benzersiz Barkod Üret", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1033, BranchCode="0", Code="ERP.MIRROR.VIEW", Name="ERP Eşlenmiş Verilerini Görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1034, BranchCode="0", Code="ERP.MIRROR.SYNC", Name="ERP Eşleme İşlemlerini Tetikle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1035, BranchCode="0", Code="ERP.NETSIS_READ.VIEW", Name="Netsis Okuma Servislerini Kullan", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1036, BranchCode="0", Code="WMS.GOODS_RECEIPT.VIEW", Name="Mal kabulleri görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1037, BranchCode="0", Code="WMS.GOODS_RECEIPT.CREATE", Name="Mal kabul oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1038, BranchCode="0", Code="WMS.GOODS_RECEIPT.UPDATE", Name="Mal kabul güncelle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1039, BranchCode="0", Code="WMS.GOODS_RECEIPT.RELEASE", Name="Mal kabulü işleme aç", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1040, BranchCode="0", Code="WMS.GOODS_RECEIPT.RECEIVE", Name="Mal kabul işle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1041, BranchCode="0", Code="WMS.GOODS_RECEIPT.COMPLETE", Name="Mal kabulü tamamla", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1042, BranchCode="0", Code="WMS.GOODS_RECEIPT.CANCEL", Name="Mal kabulü iptal et", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1043, BranchCode="0", Code="WMS.GOODS_RECEIPT.ERP_RETRY", Name="Mal kabul ERP aktarımını yeniden dene", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1044, BranchCode="0", Code="WMS.GOODS_RECEIPT.SETTINGS.VIEW", Name="Mal kabul ayarlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1045, BranchCode="0", Code="WMS.GOODS_RECEIPT.SETTINGS.MANAGE", Name="Mal kabul ayarlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1046, BranchCode="0", Code="WMS.QUALITY.SETTINGS.VIEW", Name="Kalite ayarlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1047, BranchCode="0", Code="WMS.QUALITY.SETTINGS.MANAGE", Name="Kalite ayarlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1048, BranchCode="0", Code="WMS.QUALITY.RULES.VIEW", Name="Kalite kurallarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1049, BranchCode="0", Code="WMS.QUALITY.RULES.MANAGE", Name="Kalite kurallarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1050, BranchCode="0", Code="WMS.QUALITY.INSPECTIONS.VIEW", Name="Kalite kontrollerini görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1051, BranchCode="0", Code="WMS.QUALITY.INSPECTIONS.DECIDE", Name="Kalite kararı ver", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1052, BranchCode="0", Code="WMS.QUALITY.INSPECTIONS.RELEASE", Name="Karantinadaki ürünü serbest bırak", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1053, BranchCode="0", Code="WMS.SERIAL_RULES.VIEW", Name="Seri maske kurallarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1054, BranchCode="0", Code="WMS.SERIAL_RULES.MANAGE", Name="Seri maske kurallarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1055, BranchCode="0", Code="WMS.STEEL_RECEIPT.VIEW", Name="SAC mal kabul planlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1056, BranchCode="0", Code="WMS.STEEL_RECEIPT.IMPORT", Name="SAC beklenti aktarımı yap", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1057, BranchCode="0", Code="WMS.STEEL_RECEIPT.INSPECT", Name="SAC varış kontrolü yap", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1058, BranchCode="0", Code="WMS.STEEL_RECEIPT.CONVERT", Name="SAC levhalarını ortak mal kabule aktar", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1059, BranchCode="0", Code="WMS.STEEL_RECEIPT.PUTAWAY", Name="SAC levhasını nihai rafa yerleştir", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1060, BranchCode="0", Code="WMS.STEEL_RECEIPT.VEHICLE.VIEW", Name="SAC araç girişlerini görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1061, BranchCode="0", Code="WMS.STEEL_RECEIPT.VEHICLE.MANAGE", Name="SAC araç girişlerini yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=1062, BranchCode="0", Code="WMS.WAREHOUSE_TRANSFER.VIEW", Name="Depolar arası transferleri görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1063, BranchCode="0", Code="WMS.WAREHOUSE_TRANSFER.CREATE", Name="Depolar arası transfer taslağı oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1064, BranchCode="0", Code="WMS.WAREHOUSE_TRANSFER.OPERATE", Name="Depolar arası transfer operasyonu yürüt", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=1065, BranchCode="0", Code="WMS.WAREHOUSE_TRANSFER.APPROVE", Name="Depolar arası transferi onayla", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=2000, BranchCode="0", Code="WMS.SHIPPING.VIEW", Name="Sevk kayıtlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2001, BranchCode="0", Code="WMS.SHIPPING.CREATE", Name="Sevk taslağı oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2002, BranchCode="0", Code="WMS.SHIPPING.OPERATE", Name="Toplama paketleme ve yükleme işlemlerini yürüt", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2003, BranchCode="0", Code="WMS.SHIPPING.APPROVE", Name="Sevki onayla", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2004, BranchCode="0", Code="WMS.SHIPPING.SETTINGS.VIEW", Name="Sevk ayarlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2005, BranchCode="0", Code="WMS.SHIPPING.SETTINGS.MANAGE", Name="Sevk ayarlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<BarcodePolicy>().HasData(new BarcodePolicy { Id=1, BranchCode="0", PolicyKey="GLOBAL", DisplayName="Genel Barkod Politikası", CurrentVersion=1, IsActive=true, CreatedDate=seedDate });
        modelBuilder.Entity<BarcodePolicyProfile>().HasData(
            new BarcodePolicyProfile { Id=1, BranchCode="0", BarcodePolicyId=1, Scope=BarcodePolicyScope.ProductSerial, DisplayName="Ürün / Seri", Prefix="WMS-S", Separator="/", NextSequence=1, IsEnabled=true, CreatedDate=seedDate },
            new BarcodePolicyProfile { Id=2, BranchCode="0", BarcodePolicyId=1, Scope=BarcodePolicyScope.ProductLot, DisplayName="Ürün / Lot", Prefix="WMS-L", Separator="/", NextSequence=1, IsEnabled=true, CreatedDate=seedDate },
            new BarcodePolicyProfile { Id=3, BranchCode="0", BarcodePolicyId=1, Scope=BarcodePolicyScope.Location, DisplayName="Raf / Konum", Prefix="WMS-R", Separator="/", NextSequence=1, IsEnabled=true, CreatedDate=seedDate },
            new BarcodePolicyProfile { Id=4, BranchCode="0", BarcodePolicyId=1, Scope=BarcodePolicyScope.Logistics, DisplayName="Palet / Koli / Lojistik", Prefix="WMS-P", Separator="/", NextSequence=1, IsEnabled=true, CreatedDate=seedDate },
            new BarcodePolicyProfile { Id=5, BranchCode="0", BarcodePolicyId=1, Scope=BarcodePolicyScope.Document, DisplayName="Belge / Operasyon", Prefix="WMS-B", Separator="/", NextSequence=1, IsEnabled=true, CreatedDate=seedDate });
        modelBuilder.Entity<BarcodePolicyProfileSegment>().HasData(
            Segment(1,1,1,BarcodePolicyField.StockCode,true), Segment(2,1,2,BarcodePolicyField.SerialNo,true), Segment(3,1,3,BarcodePolicyField.YapCode,false), Sequence(4,1,4),
            Segment(5,2,1,BarcodePolicyField.StockCode,true), Segment(6,2,2,BarcodePolicyField.LotNo,true), Segment(7,2,3,BarcodePolicyField.YapCode,false), Sequence(8,2,4),
            Segment(9,3,1,BarcodePolicyField.WarehouseCode,true), Segment(10,3,2,BarcodePolicyField.LocationCode,true), Sequence(11,3,3),
            Segment(12,4,1,BarcodePolicyField.DocumentNo,true), DateSegment(13,4,2), Sequence(14,4,3),
            Segment(15,5,1,BarcodePolicyField.DocumentNo,true), Sequence(16,5,2));
        modelBuilder.Entity<ProjectSetting>().HasData(new ProjectSetting { Id=1, BranchCode="0", SettingKey="GLOBAL", NumberLocale="tr-TR",
            DecimalPlaces=2, DateFormat="dd.MM.yyyy", TimeFormat="HH:mm", YearFormat="yyyy", TimeZoneId="Europe/Istanbul", CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=2100, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.VIEW", Name="Ambar girişlerini görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2101, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.CREATE", Name="Ambar girişi oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2102, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.UPDATE", Name="Ambar girişini güncelle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2103, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.RELEASE", Name="Ambar girişini işleme aç", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2104, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.RECEIVE", Name="Ambar girişini işle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2105, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.COMPLETE", Name="Ambar girişini tamamla", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2106, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.CANCEL", Name="Ambar girişini iptal et", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2107, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.SETTINGS.VIEW", Name="Ambar giriş ayarlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2108, BranchCode="0", Code="WMS.WAREHOUSE_INBOUND.SETTINGS.MANAGE", Name="Ambar giriş ayarlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2110, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.VIEW", Name="Ambar çıkışlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2111, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.CREATE", Name="Ambar çıkışı oluştur", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2112, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.UPDATE", Name="Ambar çıkışını güncelle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2113, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.DELETE", Name="Ambar çıkış taslağını sil", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2114, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.OPERATE", Name="Ambar çıkış operasyonunu yürüt", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2115, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.APPROVE", Name="Ambar çıkışını onayla", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2116, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.CANCEL", Name="Ambar çıkışını iptal et", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2117, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.SETTINGS.VIEW", Name="Ambar çıkış ayarlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2118, BranchCode="0", Code="WMS.WAREHOUSE_OUTBOUND.SETTINGS.MANAGE", Name="Ambar çıkış ayarlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionDefinition>().HasData(
            new PermissionDefinition { Id=2200, BranchCode="0", Code="WMS.PACKING.VIEW", Name="Paketlemeyi görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2201, BranchCode="0", Code="WMS.PACKING.OPERATE", Name="Paketleme operasyonu yürüt", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2202, BranchCode="0", Code="WMS.PACKING.CLOSE", Name="Paketi kapat ve serbest bırak", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2203, BranchCode="0", Code="WMS.PACKING.REOPEN", Name="Kapalı paketi yeniden aç", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2204, BranchCode="0", Code="WMS.PACKING.DEFINITIONS.VIEW", Name="Paketleme tanımlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2205, BranchCode="0", Code="WMS.PACKING.DEFINITIONS.MANAGE", Name="Paketleme tanımlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2206, BranchCode="0", Code="WMS.PACKING.SETTINGS.VIEW", Name="Paketleme ayarlarını görüntüle", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate },
            new PermissionDefinition { Id=2207, BranchCode="0", Code="WMS.PACKING.SETTINGS.MANAGE", Name="Paketleme ayarlarını yönet", IsActive=true, AvailableOnWeb=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionGroup>().HasData(new PermissionGroup { Id=1001, BranchCode="0", Name="System Administrators", Description="Tam sistem yönetimi", IsSystemAdmin=true, IsActive=true, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(1,8).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(9,7).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(16,2).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(18,2).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(20,4).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(24,6).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(30,3).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(33,11).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(44,8).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(new PermissionGroupPermission { Id=1052, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1052, CreatedDate=seedDate });
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(55,5).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(60,2).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(62,4).Select(i=>new PermissionGroupPermission { Id=1000+i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=1000+i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(2000,6).Select(i=>new PermissionGroupPermission { Id=i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(2100,9).Select(i=>new PermissionGroupPermission { Id=i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(2110,9).Select(i=>new PermissionGroupPermission { Id=i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=i, CreatedDate=seedDate }));
        modelBuilder.Entity<PermissionGroupPermission>().HasData(Enumerable.Range(2200,8).Select(i=>new PermissionGroupPermission { Id=i, BranchCode="0", PermissionGroupId=1001, PermissionDefinitionId=i, CreatedDate=seedDate }));
        modelBuilder.Entity<UserPermissionGroup>().HasData(new UserPermissionGroup { Id=1001, BranchCode="0", UserId=1, PermissionGroupId=1001, CreatedDate=seedDate });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureStockLedgerIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureStockLedgerIsAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnsureStockLedgerIsAppendOnly()
    {
        if (ChangeTracker.Entries<StockMovementOperation>().Any(x => x.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<StockMovementEntry>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Stok hareket defteri immutable yapıdadır; kayıtlar güncellenemez veya silinemez. Ters kayıt kullanın.");

        if (ChangeTracker.Entries<StockSerialRegistry>().Any(x =>
                x.State == EntityState.Deleted
                || (x.State == EntityState.Modified && x.Entity.IsDeleted)))
            throw new InvalidOperationException("Stok seri sicili silinemez. Kullanılmayacak serileri Voided durumuna alın.");

        if (ChangeTracker.Entries<GoodsReceiptStatusHistory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Mal kabul durum geçmişi immutable yapıdadır; kayıtlar güncellenemez veya silinemez.");

        if (ChangeTracker.Entries<WarehouseInboundStatusHistory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Ambar giriş durum geçmişi değiştirilemez veya silinemez.");
        if (ChangeTracker.Entries<WarehouseOutboundStatusHistory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Ambar çıkış durum geçmişi değiştirilemez veya silinemez.");
        if (ChangeTracker.Entries<WarehouseTransferStatusHistory>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Depolar arası transfer durum geçmişi immutable yapıdadır; kayıtlar güncellenemez veya silinemez.");
    }

    private static BarcodePolicyProfileSegment Segment(long id,long profileId,int order,BarcodePolicyField field,bool required)=>new(){Id=id,BranchCode="0",BarcodePolicyProfileId=profileId,Order=order,SegmentType=BarcodePolicySegmentType.Field,SourceField=field,IsRequired=required,Transform=BarcodeValueTransform.Upper,SequenceLength=8,DateFormat="yyyyMMdd",CreatedDate=new DateTime(2026,7,21,0,0,0,DateTimeKind.Utc)};
    private static BarcodePolicyProfileSegment Sequence(long id,long profileId,int order)=>new(){Id=id,BranchCode="0",BarcodePolicyProfileId=profileId,Order=order,SegmentType=BarcodePolicySegmentType.Sequence,IsRequired=true,Transform=BarcodeValueTransform.None,SequenceLength=8,DateFormat="yyyyMMdd",CreatedDate=new DateTime(2026,7,21,0,0,0,DateTimeKind.Utc)};
    private static BarcodePolicyProfileSegment DateSegment(long id,long profileId,int order)=>new(){Id=id,BranchCode="0",BarcodePolicyProfileId=profileId,Order=order,SegmentType=BarcodePolicySegmentType.Date,IsRequired=true,Transform=BarcodeValueTransform.None,SequenceLength=8,DateFormat="yyyyMMdd",CreatedDate=new DateTime(2026,7,21,0,0,0,DateTimeKind.Utc)};
}
