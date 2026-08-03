using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Shipping.Application;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.WarehouseInbound.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

/// <summary>
/// Makes the local-versus-ERP compensation decision from authoritative database state.
/// Clients never decide whether an operational document must be deleted in ERP first.
/// </summary>
public sealed class OperationCancellationCoordinator(
    IUnitOfWork unitOfWork,
    IErpCancellationService erpCancellation,
    IGoodsReceiptLifecycleService goodsReceipts,
    IWarehouseInboundLifecycleService warehouseInbounds,
    IWarehouseTransferOperationService warehouseTransfers,
    IWarehouseOutboundOperationService warehouseOutbounds,
    IShippingOperationService shipments) : IOperationCancellationCoordinator
{
    public async Task<OperationCancellationResult> CancelGoodsReceiptAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureRequest(id, request.IdempotencyKey, request.Reason);
        var header = await unitOfWork.Repository<GoodsReceiptHeader>().FindByIdAsync(id, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
        var route = OperationCancellationPolicy.Decide(
            header.ErpIntegrationStatus,
            header.Status == WarehouseOperationStatus.Cancelled,
            erpCancellationSupported: true);

        if (route == OperationCancellationRoute.AlreadyCancelled)
            return Already("GoodsReceipt", header.Id, header.DocumentNo, header.ErpIntegrationStatus);
        if (route == OperationCancellationRoute.ManualReconciliationRequired)
            throw ReconciliationRequired("mal kabul", header.ErpIntegrationStatus, erpCancellationSupported: true);
        if (route == OperationCancellationRoute.ErpCompensation)
            return await CancelErpAsync(
                ErpPostingSourceType.GoodsReceipt,
                header.Id,
                request.IdempotencyKey,
                request.Reason!,
                userId,
                cancellationToken);

        var result = await goodsReceipts.CancelAsync(id, request, userId, cancellationToken);
        return Local("GoodsReceipt", result.Id, result.DocumentNo, result.Status.ToString(),
            header.ErpIntegrationStatus, result.Replayed);
    }

    public async Task<OperationCancellationResult> CancelWarehouseInboundAsync(
        long id,
        WarehouseInboundTransitionRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureRequest(id, request.IdempotencyKey, request.Reason);
        var header = await unitOfWork.Repository<WarehouseInboundHeader>().FindByIdAsync(id, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Ambar giriş kaydı bulunamadı.");
        var route = OperationCancellationPolicy.Decide(
            header.ErpIntegrationStatus,
            header.Status == WarehouseOperationStatus.Cancelled,
            erpCancellationSupported: true);

        if (route == OperationCancellationRoute.AlreadyCancelled)
            return Already("WarehouseInbound", header.Id, header.DocumentNo, header.ErpIntegrationStatus);
        if (route == OperationCancellationRoute.ManualReconciliationRequired)
            throw ReconciliationRequired("ambar giriş", header.ErpIntegrationStatus, erpCancellationSupported: true);
        if (route == OperationCancellationRoute.ErpCompensation)
            return await CancelErpAsync(
                ErpPostingSourceType.WarehouseInbound,
                header.Id,
                request.IdempotencyKey,
                request.Reason!,
                userId,
                cancellationToken);

        var result = await warehouseInbounds.CancelAsync(id, request, userId, cancellationToken);
        return Local("WarehouseInbound", result.Id, result.DocumentNo, result.Status.ToString(),
            header.ErpIntegrationStatus, result.Replayed);
    }

    public async Task<OperationCancellationResult> CancelWarehouseTransferAsync(
        long id,
        WarehouseTransferTransitionRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureRequest(id, request.IdempotencyKey, request.Reason);
        var header = await unitOfWork.Repository<WarehouseTransferHeader>().FindByIdAsync(id, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Depolar arası transfer kaydı bulunamadı.");
        var route = OperationCancellationPolicy.Decide(
            header.ErpIntegrationStatus,
            header.Status == WarehouseTransferStatus.Cancelled,
            erpCancellationSupported: true);

        if (route == OperationCancellationRoute.AlreadyCancelled)
            return Already("WarehouseTransfer", header.Id, header.DocumentNo, header.ErpIntegrationStatus);
        if (route == OperationCancellationRoute.ManualReconciliationRequired)
            throw ReconciliationRequired("depolar arası transfer", header.ErpIntegrationStatus, erpCancellationSupported: true);
        if (route == OperationCancellationRoute.ErpCompensation)
        {
            await PersistManagerReturnLocationAsync(header, request, userId, cancellationToken);
            return await CancelErpAsync(
                ErpPostingSourceType.WarehouseTransfer,
                header.Id,
                request.IdempotencyKey,
                request.Reason!,
                userId,
                cancellationToken);
        }

        var result = await warehouseTransfers.CancelAsync(id, request, userId, cancellationToken);
        return Local("WarehouseTransfer", result.TransferId, result.DocumentNo, result.Status,
            header.ErpIntegrationStatus, result.Replayed);
    }

    public async Task<OperationCancellationResult> CancelWarehouseOutboundAsync(
        long id,
        WarehouseOutboundTransitionRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureRequest(id, request.IdempotencyKey, request.Reason);
        var header = await unitOfWork.Repository<WarehouseOutboundHeader>().FindByIdAsync(id, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Ambar çıkış kaydı bulunamadı.");
        var route = OperationCancellationPolicy.Decide(
            header.ErpIntegrationStatus,
            header.Status == WarehouseOutboundStatus.Cancelled,
            erpCancellationSupported: true);

        if (route == OperationCancellationRoute.AlreadyCancelled)
            return Already("WarehouseOutbound", header.Id, header.DocumentNo, header.ErpIntegrationStatus);
        if (route == OperationCancellationRoute.ManualReconciliationRequired)
            throw ReconciliationRequired("ambar çıkış", header.ErpIntegrationStatus, erpCancellationSupported: true);
        if (route == OperationCancellationRoute.ErpCompensation)
            return await CancelErpAsync(
                ErpPostingSourceType.WarehouseOutbound,
                header.Id,
                request.IdempotencyKey,
                request.Reason!,
                userId,
                cancellationToken);

        var result = await warehouseOutbounds.CancelAsync(id, request, userId, cancellationToken);
        return Local("WarehouseOutbound", result.WarehouseOutboundId, result.DocumentNo, result.Status,
            header.ErpIntegrationStatus, result.Replayed);
    }

    public async Task<OperationCancellationResult> CancelShipmentAsync(
        long id,
        ShipmentTransitionRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureRequest(id, request.IdempotencyKey, request.Reason);
        var header = await unitOfWork.Repository<ShipmentHeader>().FindByIdAsync(id, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
        var route = OperationCancellationPolicy.Decide(
            header.ErpIntegrationStatus,
            header.Status == ShipmentStatus.Cancelled,
            erpCancellationSupported: true);

        if (route == OperationCancellationRoute.AlreadyCancelled)
            return Already("Shipment", header.Id, header.DocumentNo, header.ErpIntegrationStatus);
        if (route == OperationCancellationRoute.ManualReconciliationRequired)
            throw ReconciliationRequired("sevk", header.ErpIntegrationStatus, erpCancellationSupported: true);
        if (route == OperationCancellationRoute.ErpCompensation)
            return await CancelErpAsync(
                ErpPostingSourceType.Shipment,
                header.Id,
                request.IdempotencyKey,
                request.Reason!,
                userId,
                cancellationToken);

        var result = await shipments.CancelAsync(id, request, userId, cancellationToken);
        return Local("Shipment", result.ShipmentId, result.DocumentNo, result.Status,
            header.ErpIntegrationStatus, result.Replayed);
    }

    private async Task<OperationCancellationResult> CancelErpAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        Guid requestedKey,
        string requestedReason,
        long userId,
        CancellationToken cancellationToken)
    {
        var posting = await unitOfWork.Repository<ErpPostingRecord>().Query()
            .SingleOrDefaultAsync(
                x => x.SourceType == sourceType && x.SourceEntityId == sourceEntityId,
                cancellationToken)
            ?? throw AppException.Conflict(
                "Kaynak kayıt ERP'ye aktarılmış görünüyor ancak doğrulanmış ERP gönderim kaydı bulunamadı. " +
                "Yerel stok geri alınmadı; önce ERP gönderim mutabakatı yapılmalıdır.");

        var prior = await unitOfWork.Repository<ErpCancellationRecord>().Query()
            .SingleOrDefaultAsync(x => x.ErpPostingRecordId == posting.Id, cancellationToken);
        var command = prior is null
            ? new CancelErpDocumentRequest(requestedKey, requestedReason.Trim())
            : new CancelErpDocumentRequest(prior.IdempotencyKey, prior.Reason);

        var result = await erpCancellation.CancelAsync(
            sourceType,
            sourceEntityId,
            command,
            userId,
            cancellationToken);
        var succeeded = result.Status == ErpCancellationStatus.Succeeded;
        return new(
            sourceType.ToString(),
            sourceEntityId,
            result.SourceDocumentNo,
            OperationCancellationRoute.ErpCompensation,
            succeeded ? "Cancelled" : "CancellationPending",
            result.Status.ToString(),
            result.ErpDeletedAtUtc.HasValue,
            result.WmsReversedAtUtc.HasValue,
            prior?.Status == ErpCancellationStatus.Succeeded,
            result.ErrorCode,
            result.ErrorMessage);
    }

    private async Task PersistManagerReturnLocationAsync(
        WarehouseTransferHeader header,
        WarehouseTransferTransitionRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        if (header.CancellationReturnPolicy != WarehouseTransferCancellationReturnPolicy.ManagerSelectionRequired)
            return;
        var returnLocationId = request.ReturnLocationId
            ?? throw AppException.BadRequest("İptal politikası gereği kaynak depodan bir iade rafı seçilmelidir.");
        var valid = await unitOfWork.Repository<WarehouseLocation>().Query().AnyAsync(x =>
            x.Id == returnLocationId && x.WarehouseId == header.SourceWarehouseId && x.IsActive && x.IsPutaway,
            cancellationToken);
        if (!valid)
            throw AppException.BadRequest("Seçilen iade rafı kaynak depoya ait, aktif ve yerleştirmeye uygun olmalıdır.");
        var tracked = await unitOfWork.Repository<WarehouseTransferHeader>().FindByIdAsync(
            header.Id, tracking: true, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Depolar arası transfer kaydı bulunamadı.");
        tracked.CancellationReturnLocationId = returnLocationId;
        tracked.UpdatedBy = userId;
        tracked.UpdatedDate = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static OperationCancellationResult Local(
        string sourceType,
        long id,
        string documentNo,
        string operationStatus,
        ErpIntegrationStatus erpStatus,
        bool replayed) =>
        new(sourceType, id, documentNo, OperationCancellationRoute.LocalCompensation,
            operationStatus, erpStatus.ToString(), false, true, replayed);

    private static OperationCancellationResult Already(
        string sourceType,
        long id,
        string documentNo,
        ErpIntegrationStatus erpStatus) =>
        new(sourceType, id, documentNo, OperationCancellationRoute.AlreadyCancelled,
            "Cancelled", erpStatus.ToString(), erpStatus == ErpIntegrationStatus.Cancelled, true, true);

    private static AppException ReconciliationRequired(
        string operationName,
        ErpIntegrationStatus status,
        bool erpCancellationSupported)
    {
        var detail = erpCancellationSupported
            ? "ERP gönderim/silme mutabakatı tamamlanmadan yerel stok, raf, seri ve rezervasyonlar geri alınamaz."
            : "Bu operasyon tipi için doğrulanmış ERP silme adaptörü henüz tanımlı değildir; belge Netsis'te doğrulanmadan yerel stok geri alınamaz.";
        return AppException.Conflict(
            $"{operationName} ERP durumu '{status}' olduğu için otomatik iptal güvenli değildir. {detail}");
    }

    private static void EnsureRequest(long id, Guid idempotencyKey, string? reason)
    {
        if (id <= 0 || idempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Kaynak kayıt ve idempotency anahtarı zorunludur.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000)
            throw AppException.BadRequest("İptal nedeni 5-1000 karakter arasında olmalıdır.");
    }
}
