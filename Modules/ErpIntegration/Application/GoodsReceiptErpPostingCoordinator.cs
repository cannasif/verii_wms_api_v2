using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class GoodsReceiptErpPostingCoordinator(
    IUnitOfWork unitOfWork,
    IErpPostingService erpPostingService) : IGoodsReceiptErpPostingCoordinator
{
    public async Task<ErpPostingResult?> PostIfEligibleAsync(
        long goodsReceiptId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        if (goodsReceiptId <= 0)
            throw AppException.BadRequest("Mal kabul kaydı seçilmelidir.");

        var header = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == goodsReceiptId, cancellationToken);

        if (header is null)
            throw AppException.NotFound("Mal kabul kaydı bulunamadı.");

        if (header.ErpIntegrationStatus is ErpIntegrationStatus.Succeeded
            or ErpIntegrationStatus.Cancelled)
        {
            return null;
        }

        if (header.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)
            throw CommitUncertain(header.DocumentNo);

        if (!GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
                header.Status,
                header.ApprovalStatus,
                header.QualityStatus,
                header.ErpPostingPolicy))
        {
            return null;
        }

        var result = await erpPostingService.PostGoodsReceiptAsync(
            goodsReceiptId,
            CreateAutomaticIdempotencyKey(goodsReceiptId),
            actorUserId > 0
                ? actorUserId
                : header.ReceivedBy ?? header.UpdatedBy ?? header.CreatedBy ?? 0,
            cancellationToken);

        return result.Status switch
        {
            ErpPostingStatus.Succeeded => result,
            ErpPostingStatus.CommitUncertain => throw CommitUncertain(header.DocumentNo),
            _ => throw PostingFailed(header.DocumentNo, result)
        };
    }

    private static AppException PostingFailed(string documentNo, ErpPostingResult result)
    {
        var detail = string.Join(
            " - ",
            new[] { result.ErrorCode, result.ErrorMessage }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        if (string.IsNullOrWhiteSpace(detail))
            detail = "Netsis servisinden başarısız yanıt alındı.";

        return AppException.BadGateway(
            $"{documentNo} numaralı mal kabul adımı WMS'te tamamlandı ancak Netsis irsaliyesi oluşturulamadı: {detail} " +
            "Mal Kabul Listesi'ndeki ERP'ye Gönder işlemiyle tekrar deneyebilirsiniz.");
    }

    private static AppException CommitUncertain(string documentNo)
    {
        return AppException.Conflict(
            $"{documentNo} numaralı mal kabul adımı WMS'te tamamlandı ancak Netsis yanıtı kesinleşmedi. " +
            "Mükerrer irsaliye riskini önlemek için Netsis kontrol edilmeden tekrar göndermeyin.");
    }

    private static Guid CreateAutomaticIdempotencyKey(long goodsReceiptId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"goods-receipt:{goodsReceiptId}:automatic-erp-posting:v1"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
