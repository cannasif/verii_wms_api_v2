using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Quality.Application;

using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

public sealed class QualityService(IUnitOfWork uow, IAuditLogWriter audit, IStockMovementService stockMovement) : IQualityService, IQualityPolicyResolver
{
    private IGenericRepository<QualityParameter> Parameters => uow.Repository<QualityParameter>();
    private IGenericRepository<QualityRule> Rules => uow.Repository<QualityRule>();
    private IGenericRepository<QualityInspection> Inspections => uow.Repository<QualityInspection>();

    public async Task<QualityParameterDto> GetParametersAsync(string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var value = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == branch && x.ParameterKey == "DEFAULT", false, ct) ?? Default(branch);
        return Map(value);
    }

    public async Task<QualityParameterDto> UpdateParametersAsync(UpdateQualityParameterRequest request, long actor, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(request.BranchCode); await ValidateLocations(request, branch, ct);
        var entity = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == branch && x.ParameterKey == "DEFAULT", true, ct);
        var before = entity is null ? null : Map(entity);
        if (entity is null) { entity = Default(branch); entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; await Parameters.AddAsync(entity, ct); }
        entity.AutoCreateInspectionOnReceipt = request.AutoCreateInspectionOnReceipt; entity.DefaultInspectionMode = request.DefaultInspectionMode;
        entity.DefaultFailAction = request.DefaultFailAction; entity.HoldInventoryUntilDecision = request.HoldInventoryUntilDecision;
        entity.BlockPutawayUntilDecision = request.BlockPutawayUntilDecision; entity.BlockErpPostingUntilDecision = request.BlockErpPostingUntilDecision;
        entity.RequireManagerApprovalForRelease = request.RequireManagerApprovalForRelease; entity.AllowPartialDecision = request.AllowPartialDecision;
        entity.AllowDirectReceiptWhenNoRule = request.AllowDirectReceiptWhenNoRule; entity.BlockReceiptWhenLotMissing = request.BlockReceiptWhenLotMissing;
        entity.BlockReceiptWhenSerialMissing = request.BlockReceiptWhenSerialMissing; entity.BlockReceiptWhenExpiryMissing = request.BlockReceiptWhenExpiryMissing;
        entity.DefaultQualityLocationId = request.DefaultQualityLocationId; entity.DefaultQuarantineLocationId = request.DefaultQuarantineLocationId; entity.DefaultRejectLocationId = request.DefaultRejectLocationId;
        entity.UpdatedBy = actor; entity.UpdatedDate = DateTime.UtcNow; await uow.SaveChangesAsync(ct); var result = Map(entity);
        await audit.WriteAsync(new("quality.parameters.update", nameof(QualityParameter), entity.Id.ToString(), "Succeeded", "quality", OldValues: before, NewValues: result, ChangedFields: ["Parameters"]), ct); return result;
    }

    public async Task<PagedResponse<QualityRuleGridRow>> GetRulesPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var joined = from rule in Rules.Query()
                     join stock in uow.Repository<StockEntity>().Query() on rule.StockId equals stock.Id into stocks
                     from stock in stocks.DefaultIfEmpty()
                     select new { Rule=rule, Stock=stock };
        var q = joined.Select(x => new QualityRuleGridRow { Id=x.Rule.Id, BranchCode=x.Rule.BranchCode, ScopeType=x.Rule.ScopeType, StockId=x.Rule.StockId,
            StockCode=x.Stock==null?null:x.Stock.ErpStockCode, StockName=x.Stock==null?null:x.Stock.StockName, StockGroupCode=x.Rule.StockGroupCode,
            InspectionMode=x.Rule.InspectionMode.ToString(), SamplingMode=x.Rule.SamplingMode.ToString(), SamplingValue=x.Rule.SamplingValue,
            FailAction=x.Rule.FailAction.ToString(), AutoQuarantine=x.Rule.AutoQuarantine, RequireLot=x.Rule.RequireLot, RequireSerial=x.Rule.RequireSerial,
            RequireExpiryDate=x.Rule.RequireExpiryDate, MinimumRemainingShelfLifeDays=x.Rule.MinimumRemainingShelfLifeDays, IsActive=x.Rule.IsActive,
            Description=x.Rule.Description, CreatedBy=x.Rule.CreatedBy, CreatedDate=x.Rule.CreatedDate, UpdatedBy=x.Rule.UpdatedBy, UpdatedDate=x.Rule.UpdatedDate });
        var search=request.Search?.Trim(); q=q.Where(x=>string.IsNullOrWhiteSpace(search)||(x.StockCode!=null&&x.StockCode.Contains(search))||(x.StockName!=null&&x.StockName.Contains(search))||(x.StockGroupCode!=null&&x.StockGroupCode.Contains(search)));
        return await q.ApplyAdvancedFilters(request).ApplySort(request,nameof(QualityRuleGridRow.Id)).ToPagedResponseAsync(request,ct);
    }

    public async Task<long> CreateRuleAsync(QualityRuleUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity=new QualityRule(); await ApplyRule(entity,request,null,ct); entity.CreatedBy=actor; entity.CreatedDate=DateTime.UtcNow; await Rules.AddAsync(entity,ct); await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("quality.rule.create",nameof(QualityRule),entity.Id.ToString(),"Succeeded","quality",NewValues:Snapshot(entity),ChangedFields:["Rule"]),ct); return entity.Id;
    }

    public async Task UpdateRuleAsync(long id, QualityRuleUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity=await Rules.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Kalite kuralı bulunamadı."); var before=Snapshot(entity); await ApplyRule(entity,request,id,ct);
        entity.UpdatedBy=actor; entity.UpdatedDate=DateTime.UtcNow; await uow.SaveChangesAsync(ct); await audit.WriteAsync(new("quality.rule.update",nameof(QualityRule),id.ToString(),"Succeeded","quality",OldValues:before,NewValues:Snapshot(entity),ChangedFields:["Rule"]),ct);
    }

    public async Task DeleteRuleAsync(long id,long actor,CancellationToken ct=default)
    {
        var entity=await Rules.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Kalite kuralı bulunamadı."); entity.IsActive=false; entity.DeletedBy=actor; await Rules.SoftDeleteAsync(id,ct); await uow.SaveChangesAsync(ct);
    }

    public async Task<PagedResponse<QualityInspectionGridRow>> GetInspectionsPagedAsync(PagedRequest request,CancellationToken ct=default)
    {
        var joined=from i in Inspections.Query()
                   join w in uow.Repository<WarehouseEntity>().Query() on i.WarehouseId equals w.Id into ws
                   from w in ws.DefaultIfEmpty()
                   join g in uow.Repository<GoodsReceiptHeader>().Query() on new { Type=i.SourceDocumentType, Id=i.SourceDocumentId }
                       equals new { Type="GoodsReceipt", Id=g.Id } into gs
                   from g in gs.DefaultIfEmpty()
                   join u in uow.Repository<User>().Query() on i.CreatedBy equals (long?)u.Id into users
                   from u in users.DefaultIfEmpty()
                   join d in uow.Repository<UserDetail>().Query() on u.Id equals d.UserId into details
                   from d in details.DefaultIfEmpty()
                   where i.QueuedAtUtc != null
                   select new { Inspection=i, Warehouse=w, Receipt=g, User=u, Detail=d };
        var q=joined.Select(x=>new QualityInspectionGridRow { Id=x.Inspection.Id,BranchCode=x.Inspection.BranchCode,InspectionNo=x.Inspection.InspectionNo,
            SourceDocumentType=x.Inspection.SourceDocumentType,SourceDocumentId=x.Inspection.SourceDocumentId,SourceDocumentNo=x.Inspection.SourceDocumentNo,
            WarehouseId=x.Inspection.WarehouseId,WarehouseCode=x.Warehouse==null?null:x.Warehouse.WarehouseCode,
            WarehouseName=x.Warehouse==null?null:x.Warehouse.WarehouseName,SupplierId=x.Inspection.SupplierId,
            SourceWaybillNo=x.Receipt==null?null:(x.Receipt.ElectronicWaybillNo??x.Receipt.WaybillNo),
            CreatedByName=x.User==null?null:(x.Detail==null?x.User.Username:(x.Detail.FirstName+" "+x.Detail.LastName)),
            Status=x.Inspection.Status.ToString(),LineCount=x.Inspection.Lines.Count,TotalQuantity=x.Inspection.Lines.Sum(line=>line.Quantity),
            CreatedAtUtc=x.Inspection.CreatedAtUtc,QueuedAtUtc=x.Inspection.QueuedAtUtc,DecidedAtUtc=x.Inspection.DecidedAtUtc,InspectorUserId=x.Inspection.InspectorUserId,
            CreatedBy=x.Inspection.CreatedBy,CreatedDate=x.Inspection.CreatedDate,UpdatedBy=x.Inspection.UpdatedBy,UpdatedDate=x.Inspection.UpdatedDate });
        var search=request.Search?.Trim(); q=q.Where(x=>string.IsNullOrWhiteSpace(search)||x.InspectionNo.Contains(search)||x.SourceDocumentNo.Contains(search)
            ||(x.SourceWaybillNo!=null&&x.SourceWaybillNo.Contains(search))||(x.CreatedByName!=null&&x.CreatedByName.Contains(search))
            ||(x.WarehouseName!=null&&x.WarehouseName.Contains(search)));
        return await q.ApplyAdvancedFilters(request).ApplySort(request,nameof(QualityInspectionGridRow.QueuedAtUtc)).ToPagedResponseAsync(request,ct);
    }

    public async Task<QualityInspectionDetail> GetInspectionAsync(long id, CancellationToken ct = default)
    {
        var inspection = await Inspections.Query().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("Kalite kontrolü bulunamadı.");
        var warehouse = await uow.Repository<WarehouseEntity>().Query().Where(x => x.Id == inspection.WarehouseId)
            .Select(x => new { x.WarehouseCode, x.WarehouseName }).FirstOrDefaultAsync(ct);
        var receipt = inspection.SourceDocumentType == "GoodsReceipt"
            ? await uow.Repository<GoodsReceiptHeader>().Query().Where(x => x.Id == inspection.SourceDocumentId)
                .Select(x => new { x.WaybillNo, x.ElectronicWaybillNo }).FirstOrDefaultAsync(ct)
            : null;
        var creator = inspection.CreatedBy.HasValue
            ? await (from user in uow.Repository<User>().Query()
                     join detail in uow.Repository<UserDetail>().Query() on user.Id equals detail.UserId into details
                     from detail in details.DefaultIfEmpty()
                     where user.Id == inspection.CreatedBy.Value
                     select detail == null ? user.Username : detail.FirstName + " " + detail.LastName).FirstOrDefaultAsync(ct)
            : null;
        var header = new QualityInspectionGridRow { Id = inspection.Id, BranchCode = inspection.BranchCode,
            InspectionNo = inspection.InspectionNo, SourceDocumentType = inspection.SourceDocumentType,
            SourceDocumentId = inspection.SourceDocumentId, SourceDocumentNo = inspection.SourceDocumentNo,
            WarehouseId = inspection.WarehouseId, WarehouseCode = warehouse?.WarehouseCode, WarehouseName = warehouse?.WarehouseName,
            SupplierId = inspection.SupplierId, SourceWaybillNo = receipt == null ? null : receipt.ElectronicWaybillNo ?? receipt.WaybillNo,
            CreatedByName = creator, Status = inspection.Status.ToString(), LineCount = inspection.Lines.Count,
            TotalQuantity = inspection.Lines.Sum(x => x.Quantity), CreatedAtUtc = inspection.CreatedAtUtc,
            QueuedAtUtc = inspection.QueuedAtUtc, DecidedAtUtc = inspection.DecidedAtUtc, InspectorUserId = inspection.InspectorUserId,
            CreatedBy = inspection.CreatedBy, CreatedDate = inspection.CreatedDate, UpdatedBy = inspection.UpdatedBy, UpdatedDate = inspection.UpdatedDate };
        var lines = inspection.Lines.OrderBy(x => x.Id).Select(x => new QualityInspectionLineDto(x.Id, x.GoodsReceiptLineId,
            x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.YapCodeSnapshot, x.LotNo, x.SerialNo, x.ExpiryDate,
            x.Quantity, x.SampleQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.QuarantineQuantity, x.Decision,
            x.ReasonCode, x.ReasonNote, x.DecisionBy, x.DecisionAtUtc)).ToList();
        var parameter = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == inspection.BranchCode && x.ParameterKey == "DEFAULT", false, ct)
            ?? Default(inspection.BranchCode);
        return new QualityInspectionDetail(header, lines, inspection.Note, inspection.RowVersion,
            parameter.AllowPartialDecision, parameter.RequireManagerApprovalForRelease);
    }

    public Task DecideInspectionAsync(long id, DecideQualityInspectionRequest request, long actor,
        bool canReleaseQuarantine, CancellationToken ct = default)
    {
        if (request.IdempotencyKey == Guid.Empty || request.Decision is QualityDecision.Pending or QualityDecision.Hold)
            throw AppException.BadRequest("Nihai karar kabul, ret, karantina veya tedarikçiye iade olmalıdır.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var inspection = await Inspections.Query(true).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("Kalite kontrolü bulunamadı.");
            ApplyVersion(inspection, request.RowVersion);
            if (inspection.Status == QualityInspectionStatus.Cancelled) throw AppException.Conflict("İptal edilmiş kalite kontrolü sonuçlandırılamaz.");
            if (!string.Equals(inspection.SourceDocumentType, "GoodsReceipt", StringComparison.OrdinalIgnoreCase))
                throw AppException.Conflict("Bu kaynak türü için fiziksel kalite kararı henüz desteklenmiyor.");

            var gr = await uow.Repository<GoodsReceiptHeader>().Query(true).FirstOrDefaultAsync(x => x.Id == inspection.SourceDocumentId, token)
                ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
            var parameter = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == inspection.BranchCode && x.ParameterKey == "DEFAULT", false, token)
                ?? Default(inspection.BranchCode);
            var requestedIds = request.LineIds?.Where(x => x > 0).Distinct().ToHashSet();
            var eligible = inspection.Lines.Where(x => x.Decision is QualityDecision.Pending or QualityDecision.Hold or QualityDecision.Quarantined).ToList();
            var selected = requestedIds is { Count: > 0 } ? eligible.Where(x => requestedIds.Contains(x.Id)).ToList() : eligible;
            if (selected.Count == 0 || requestedIds is { Count: > 0 } && selected.Count != requestedIds.Count)
                throw AppException.BadRequest("Seçilen kalite satırlarından biri bulunamadı veya daha önce sonuçlandırılmış.");
            if (selected.Count != eligible.Count && !parameter.AllowPartialDecision)
                throw AppException.Conflict("Kalite ayarlarında kısmi karar kapalı; bekleyen satırların tamamı seçilmelidir.");

            var releasesQuarantine = request.Decision == QualityDecision.Accepted && selected.Any(x => x.Decision == QualityDecision.Quarantined);
            if (releasesQuarantine && parameter.RequireManagerApprovalForRelease && !canReleaseQuarantine)
                throw AppException.Forbidden("Karantinadan serbest bırakma için yönetici izni gerekir.");
            var grLineIds = selected.Where(x => x.GoodsReceiptLineId.HasValue).Select(x => x.GoodsReceiptLineId!.Value).Distinct().ToArray();
            var grLines = await uow.Repository<GoodsReceiptLine>().Query(true).Where(x => grLineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            if (grLines.Count != grLineIds.Length) throw AppException.Conflict("Kalite satırının mal kabul bağlantısı eksik.");

            long? targetLocation = request.Decision switch
            {
                QualityDecision.Quarantined => parameter.DefaultQuarantineLocationId,
                QualityDecision.Rejected => parameter.DefaultRejectLocationId,
                _ => null
            };
            if (request.Decision is QualityDecision.Quarantined or QualityDecision.Rejected && !targetLocation.HasValue)
                throw AppException.Conflict("Seçilen kalite kararı için hedef kalite rafı ayarlarda tanımlı değil.");

            var movementLines = selected.Select(line =>
            {
                var receiptLine = grLines[line.GoodsReceiptLineId!.Value];
                var receiptLocation = receiptLine.DefaultReceivingLocationId ?? gr.ReceivingLocationId;
                var wasQuarantined = line.Decision == QualityDecision.Quarantined;
                var sourceLocation = wasQuarantined
                    ? parameter.DefaultQuarantineLocationId ?? throw AppException.Conflict("Karantina rafı ayarlarda tanımlı değil.")
                    : receiptLocation;
                var sourceStatus = wasQuarantined ? "Quarantine" : gr.HoldInventoryUntilQualityDecision ? "QualityHold" : "Available";
                return request.Decision == QualityDecision.Returned
                    ? new StockMovementLineRequest(line.StockId, line.YapCodeId, line.Quantity, gr.TargetWarehouseId, sourceLocation,
                        null, null, receiptLine.UnitCode, line.LotNo, line.SerialNo, sourceStatus, sourceStatus, null)
                    : new StockMovementLineRequest(line.StockId, line.YapCodeId, line.Quantity, gr.TargetWarehouseId, sourceLocation,
                        gr.TargetWarehouseId, targetLocation ?? receiptLocation, receiptLine.UnitCode, line.LotNo, line.SerialNo,
                        sourceStatus, sourceStatus, request.Decision switch
                        { QualityDecision.Accepted => "Available", QualityDecision.Quarantined => "Quarantine", QualityDecision.Rejected => "Rejected", _ => sourceStatus });
            }).Where(x => request.Decision != QualityDecision.Accepted || !string.Equals(x.SourceStockStatus, "Available", StringComparison.OrdinalIgnoreCase)).ToList();
            StockMovementPostResult? movement = null;
            if (movementLines.Count > 0)
                movement = await stockMovement.PostAsync(new PostStockMovementRequest($"QUALITY:{inspection.Id}:{request.IdempotencyKey:N}",
                    request.Decision == QualityDecision.Returned ? StockMovementTypes.SupplierReturn : StockMovementTypes.Transfer,
                    "QualityInspection", inspection.InspectionNo, inspection.Id, DateTime.UtcNow, "QualityDisposition", request.Note, movementLines), token);

            var now = DateTimeOffset.UtcNow;
            foreach (var line in selected)
            {
                var receiptLine = grLines[line.GoodsReceiptLineId!.Value];
                receiptLine.QuarantineQuantity = Math.Max(0, receiptLine.QuarantineQuantity - line.Quantity);
                if (request.Decision == QualityDecision.Accepted) receiptLine.AcceptedQuantity += line.Quantity;
                else if (request.Decision is QualityDecision.Rejected or QualityDecision.Returned) receiptLine.RejectedQuantity += line.Quantity;
                else receiptLine.QuarantineQuantity += line.Quantity;
                line.Decision = request.Decision; line.DecisionBy = actor; line.DecisionAtUtc = now;
                line.ReasonCode = Clean(request.ReasonCode, 100); line.ReasonNote = Clean(request.Note, 1000);
                line.AcceptedQuantity = request.Decision == QualityDecision.Accepted ? line.Quantity : 0;
                line.RejectedQuantity = request.Decision is QualityDecision.Rejected or QualityDecision.Returned ? line.Quantity : 0;
                line.QuarantineQuantity = request.Decision == QualityDecision.Quarantined ? line.Quantity : 0;
            }
            var pending = inspection.Lines.Count(x => x.Decision is QualityDecision.Pending or QualityDecision.Hold);
            var accepted = inspection.Lines.Count(x => x.Decision == QualityDecision.Accepted);
            var quarantined = inspection.Lines.Count(x => x.Decision == QualityDecision.Quarantined);
            var failed = inspection.Lines.Count(x => x.Decision is QualityDecision.Rejected or QualityDecision.Returned);
            inspection.Status = pending > 0 || accepted > 0 && quarantined > 0 ? QualityInspectionStatus.PartiallyDecided
                : failed > 0 ? QualityInspectionStatus.Failed
                : quarantined > 0 ? QualityInspectionStatus.Quarantined
                : releasesQuarantine ? QualityInspectionStatus.Released : QualityInspectionStatus.Passed;
            inspection.DecidedAtUtc = pending == 0 ? now : null;
            inspection.InspectorUserId = actor; inspection.Note = Clean(request.Note, 1000);
            inspection.UpdatedBy = actor; inspection.UpdatedDate = DateTime.UtcNow;
            gr.QualityStatus = pending > 0 ? OperationQualityStatus.PartiallyCompleted
                : failed > 0 ? OperationQualityStatus.Failed
                : quarantined > 0 ? OperationQualityStatus.InProgress : OperationQualityStatus.Passed;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("quality.inspection.decide", nameof(QualityInspection), id.ToString(), "Succeeded", "quality",
                NewValues: new { request.IdempotencyKey, request.Decision, request.LineIds, request.ReasonCode, MovementId = movement?.OperationId },
                ChangedFields: ["Status", "Lines", "InventoryStatus"]), token);
            return true;
        }, ct);
    }

    private Task DecideInspectionLegacyAsync(long id, DecideQualityInspectionRequest request,long actor,CancellationToken ct=default)
    {
        if(request.Decision is QualityDecision.Pending or QualityDecision.Hold) throw AppException.BadRequest("Nihai karar kabul, ret, karantina veya tedarikçiye iade olmalıdır.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var inspection=await Inspections.Query(true).Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,token)??throw AppException.NotFound("Kalite kontrolü bulunamadı.");
            if(inspection.Status is QualityInspectionStatus.Passed or QualityInspectionStatus.Failed or QualityInspectionStatus.Released or QualityInspectionStatus.Cancelled) throw AppException.Conflict("Sonuçlanmış kalite kontrolü yeniden karara bağlanamaz.");
            if(!string.Equals(inspection.SourceDocumentType,"GoodsReceipt",StringComparison.OrdinalIgnoreCase)) throw AppException.Conflict("Bu kaynak türü için fiziksel kalite kararı henüz desteklenmiyor.");
            var gr=await uow.Repository<GoodsReceiptHeader>().Query(true).FirstOrDefaultAsync(x=>x.Id==inspection.SourceDocumentId,token)??throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
            var parameter=await Parameters.FirstOrDefaultAsync(x=>x.BranchCode==inspection.BranchCode&&x.ParameterKey=="DEFAULT",false,token)??Default(inspection.BranchCode);
            var grLineIds=inspection.Lines.Where(x=>x.GoodsReceiptLineId.HasValue).Select(x=>x.GoodsReceiptLineId!.Value).Distinct().ToArray();
            var grLines=await uow.Repository<GoodsReceiptLine>().Query().Where(x=>grLineIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
            if(grLines.Count!=grLineIds.Length) throw AppException.Conflict("Kalite satırının mal kabul bağlantısı eksik.");
            long? targetLocation=request.Decision switch { QualityDecision.Quarantined=>parameter.DefaultQuarantineLocationId, QualityDecision.Rejected=>parameter.DefaultRejectLocationId, _=>null };
            if(request.Decision is (QualityDecision.Quarantined or QualityDecision.Rejected) && !targetLocation.HasValue) throw AppException.Conflict("Seçilen kalite kararı için hedef kalite rafı ayarlarda tanımlı değil.");
            var wasQuarantined=inspection.Status==QualityInspectionStatus.Quarantined;
            var sourceStatus=wasQuarantined?"Quarantine":gr.HoldInventoryUntilQualityDecision?"QualityHold":"Available";
            var movementLines=inspection.Lines.Select(line =>
            {
                var receiptLine=grLines[line.GoodsReceiptLineId!.Value];
                var receiptLocation=receiptLine.DefaultReceivingLocationId??gr.ReceivingLocationId;
                var sourceLocation=wasQuarantined?(parameter.DefaultQuarantineLocationId??throw AppException.Conflict("Karantina rafı ayarlarda tanımlı değil.")):receiptLocation;
                return request.Decision==QualityDecision.Returned
                    ? new StockMovementLineRequest(line.StockId,line.YapCodeId,line.Quantity,gr.TargetWarehouseId,sourceLocation,null,null,receiptLine.UnitCode,line.LotNo,line.SerialNo,sourceStatus,sourceStatus,null)
                    : new StockMovementLineRequest(line.StockId,line.YapCodeId,line.Quantity,gr.TargetWarehouseId,sourceLocation,gr.TargetWarehouseId,targetLocation??receiptLocation,receiptLine.UnitCode,line.LotNo,line.SerialNo,sourceStatus,sourceStatus,
                        request.Decision switch { QualityDecision.Accepted=>"Available",QualityDecision.Quarantined=>"Quarantine",QualityDecision.Rejected=>"Rejected",_=>sourceStatus });
            }).ToList();
            var needsMovement=request.Decision!=QualityDecision.Accepted||!string.Equals(sourceStatus,"Available",StringComparison.OrdinalIgnoreCase);
            StockMovementPostResult? movement=null;
            if(needsMovement) movement=await stockMovement.PostAsync(new PostStockMovementRequest($"QUALITY:{inspection.Id}:{request.Decision}",request.Decision==QualityDecision.Returned?StockMovementTypes.SupplierReturn:StockMovementTypes.Transfer,
                "QualityInspection",inspection.InspectionNo,inspection.Id,DateTime.UtcNow,"QualityDisposition",request.Note,movementLines),token);
            var now=DateTimeOffset.UtcNow;
            var decisionLines=wasQuarantined?inspection.Lines:inspection.Lines.Where(x=>x.Decision is QualityDecision.Pending or QualityDecision.Hold);
            foreach(var line in decisionLines) { line.Decision=request.Decision; line.DecisionBy=actor; line.DecisionAtUtc=now; line.ReasonCode=Clean(request.ReasonCode,100); line.ReasonNote=Clean(request.Note,1000); line.AcceptedQuantity=request.Decision==QualityDecision.Accepted?line.Quantity:0; line.RejectedQuantity=request.Decision is QualityDecision.Rejected or QualityDecision.Returned?line.Quantity:0; line.QuarantineQuantity=request.Decision==QualityDecision.Quarantined?line.Quantity:0; }
            inspection.Status=request.Decision switch { QualityDecision.Accepted=>QualityInspectionStatus.Passed, QualityDecision.Quarantined=>QualityInspectionStatus.Quarantined, _=>QualityInspectionStatus.Failed };
            inspection.DecidedAtUtc=now; inspection.InspectorUserId=actor; inspection.Note=Clean(request.Note,1000); inspection.UpdatedBy=actor; inspection.UpdatedDate=DateTime.UtcNow;
            gr.QualityStatus=request.Decision switch { QualityDecision.Accepted=>OperationQualityStatus.Passed,QualityDecision.Quarantined=>OperationQualityStatus.InProgress,_=>OperationQualityStatus.Failed };
            await uow.SaveChangesAsync(token); await audit.WriteAsync(new("quality.inspection.decide",nameof(QualityInspection),id.ToString(),"Succeeded","quality",NewValues:new{request.Decision,request.ReasonCode,MovementId=movement?.OperationId},ChangedFields:["Status","Lines","InventoryStatus"]),token);
            return true;
        },ct);
    }

    public async Task<ResolvedQualityPolicy> ResolveAsync(string branchCode,long stockId,string? stockGroupCode,CancellationToken ct=default)
    {
        var branch=NormalizeBranch(branchCode); var parameter=await Parameters.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.ParameterKey=="DEFAULT",false,ct)??Default(branch);
        var rule=await Rules.Query().Where(x=>x.BranchCode==branch&&x.IsActive&&(x.StockId==stockId||(x.StockId==null&&x.StockGroupCode==stockGroupCode))).OrderByDescending(x=>x.StockId==stockId).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        return rule is null ? new("GlobalParameter",null,parameter.DefaultInspectionMode,QualitySamplingMode.All,100,parameter.DefaultFailAction,false,false,false,false,null,parameter.HoldInventoryUntilDecision,parameter.BlockPutawayUntilDecision,parameter.BlockErpPostingUntilDecision)
            : new(rule.StockId.HasValue?"StockRule":"StockGroupRule",rule.Id,rule.InspectionMode,rule.SamplingMode,rule.SamplingValue,rule.FailAction,rule.AutoQuarantine,rule.RequireLot,rule.RequireSerial,rule.RequireExpiryDate,rule.MinimumRemainingShelfLifeDays,parameter.HoldInventoryUntilDecision,parameter.BlockPutawayUntilDecision,parameter.BlockErpPostingUntilDecision);
    }

    private async Task ApplyRule(QualityRule entity,QualityRuleUpsertRequest r,long? currentId,CancellationToken ct)
    {
        var branch=NormalizeBranch(r.BranchCode); var scope=r.ScopeType?.Trim()??""; if(!QualityRuleScopeTypes.All.Contains(scope)) throw AppException.BadRequest("Geçersiz kalite kapsamı.");
        if(r.SamplingValue<=0||(r.SamplingMode==QualitySamplingMode.Percentage&&r.SamplingValue>100)||r.MinimumRemainingShelfLifeDays<0) throw AppException.BadRequest("Geçersiz örnekleme veya raf ömrü değeri.");
        if(scope.Equals(QualityRuleScopeTypes.Stock,StringComparison.OrdinalIgnoreCase)) { if(!r.StockId.HasValue||!await uow.Repository<StockEntity>().AnyAsync(x=>x.Id==r.StockId&&x.BranchCode==branch,ct)) throw AppException.BadRequest("Stok bulunamadı."); }
        else if(string.IsNullOrWhiteSpace(r.StockGroupCode)) throw AppException.BadRequest("Stok grup kodu zorunludur.");
        if(await Rules.AnyAsync(x=>x.Id!=currentId&&x.BranchCode==branch&&x.ScopeType==scope&&x.StockId==r.StockId&&x.StockGroupCode==Clean(r.StockGroupCode,50)&&x.IsActive,ct)) throw AppException.Conflict("Bu kapsam için aktif kalite kuralı zaten var.");
        entity.BranchCode=branch; entity.ScopeType=scope; entity.StockId=scope.Equals(QualityRuleScopeTypes.Stock,StringComparison.OrdinalIgnoreCase)?r.StockId:null; entity.StockGroupCode=scope.Equals(QualityRuleScopeTypes.StockGroup,StringComparison.OrdinalIgnoreCase)?Clean(r.StockGroupCode,50):null;
        entity.InspectionMode=r.InspectionMode; entity.SamplingMode=r.SamplingMode; entity.SamplingValue=r.SamplingValue; entity.FailAction=r.FailAction; entity.AutoQuarantine=r.AutoQuarantine; entity.RequireLot=r.RequireLot; entity.RequireSerial=r.RequireSerial; entity.RequireExpiryDate=r.RequireExpiryDate; entity.MinimumRemainingShelfLifeDays=r.MinimumRemainingShelfLifeDays; entity.IsActive=r.IsActive; entity.Description=Clean(r.Description,500);
    }
    private async Task ValidateLocations(UpdateQualityParameterRequest r,string branch,CancellationToken ct)
    {
        var ids=new[]{r.DefaultQualityLocationId,r.DefaultQuarantineLocationId,r.DefaultRejectLocationId}
            .Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToArray();
        var locations=await uow.Repository<WarehouseLocation>().Query().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        if(locations.Count!=ids.Length||locations.Values.Any(x=>!x.IsActive||x.BranchCode!=branch))
            throw AppException.BadRequest("Kalite lokasyonları aktif ve aynı şubede olmalıdır.");
        foreach(var id in new[]{r.DefaultQuarantineLocationId,r.DefaultRejectLocationId}.Where(x=>x.HasValue).Select(x=>x!.Value))
            if(!locations[id].IsQuarantine)
                throw AppException.BadRequest("Karantina ve ret hedefleri karantina tipi raf olmalıdır.");
        if(r.DefaultQualityLocationId.HasValue&&locations[r.DefaultQualityLocationId.Value].IsPickable)
            throw AppException.BadRequest("Kalite bekleme rafı toplama işlemine açık olamaz.");
    }
    private static QualityParameter Default(string branch)=>new(){BranchCode=branch,ParameterKey="DEFAULT"};
    private static QualityParameterDto Map(QualityParameter x)=>new(x.Id,x.BranchCode,x.AutoCreateInspectionOnReceipt,x.DefaultInspectionMode,x.DefaultFailAction,x.HoldInventoryUntilDecision,x.BlockPutawayUntilDecision,x.BlockErpPostingUntilDecision,x.RequireManagerApprovalForRelease,x.AllowPartialDecision,x.AllowDirectReceiptWhenNoRule,x.BlockReceiptWhenLotMissing,x.BlockReceiptWhenSerialMissing,x.BlockReceiptWhenExpiryMissing,x.DefaultQualityLocationId,x.DefaultQuarantineLocationId,x.DefaultRejectLocationId,x.UpdatedBy,x.UpdatedDate);
    private static object Snapshot(QualityRule x)=>new{x.Id,x.BranchCode,x.ScopeType,x.StockId,x.StockGroupCode,x.InspectionMode,x.SamplingMode,x.SamplingValue,x.FailAction,x.AutoQuarantine,x.RequireLot,x.RequireSerial,x.RequireExpiryDate,x.MinimumRemainingShelfLifeDays,x.IsActive,x.Description};
    private static string NormalizeBranch(string? x)=>string.IsNullOrWhiteSpace(x)?"0":x.Trim(); private static string? Clean(string? x,int max){var v=string.IsNullOrWhiteSpace(x)?null:x.Trim();return v?.Length>max?v[..max]:v;}
    private static void ApplyVersion(QualityInspection entity,string? supplied){if(string.IsNullOrWhiteSpace(supplied))return;try{entity.RowVersion=Convert.FromBase64String(supplied);}catch{throw AppException.Conflict("Kalite kaydı güncellik bilgisi geçersiz. Sayfayı yenileyin.");}}
}
