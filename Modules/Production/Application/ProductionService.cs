using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity=verii_wms_api_v2.Modules.Stock.Domain.Stock;
using CustomerEntity=verii_wms_api_v2.Modules.Customer.Domain.Customer;
using WarehouseEntity=verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity=verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.Production.Application;

public sealed class ProductionService(
    IUnitOfWork uow,
    IDocumentNumberAllocator numberAllocator,
    IStockTrackingPolicyResolver trackingPolicyResolver,
    IAuditLogWriter audit,
    INetsisReadService netsisRead) : IProductionService
{
    private IGenericRepository<ProductionHeader> Headers => uow.Repository<ProductionHeader>();

    public async Task<PreparedNetsisProductionWorkOrder> PrepareNetsisWorkOrderAsync(
        string workOrderNumber,
        string branchCode,
        CancellationToken ct=default)
    {
        var externalNo=workOrderNumber?.Trim();
        if(string.IsNullOrWhiteSpace(externalNo))
            throw AppException.BadRequest("Netsis iş emri numarası zorunludur.");
        var branch=branchCode.Trim();
        if(!int.TryParse(branch,out var branchNumber))
            throw AppException.BadRequest("Oturum şube kodu sayısal değildir.");

        var workOrders=await netsisRead.GetProductionWorkOrdersAsync(externalNo,branchNumber,true,20,ct);
        var workOrder=workOrders.SingleOrDefault(x=>
            string.Equals(x.WorkOrderNumber,externalNo,StringComparison.OrdinalIgnoreCase))
            ??throw AppException.NotFound("Netsis iş emri bulunamadı.");
        var recipe=await netsisRead.GetProductionWorkOrderRecipeAsync(externalNo,branchNumber,ct);
        if(recipe.Count==0)
            throw AppException.Conflict("Netsis iş emrinin reçete bileşeni bulunamadı.");

        var stockCodes=recipe.Select(x=>x.ComponentStockCode)
            .Append(workOrder.StockCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stocks=await uow.Repository<StockEntity>().Query()
            .Where(x=>x.BranchCode==branch&&stockCodes.Contains(x.ErpStockCode))
            .ToListAsync(ct);
        var stockMap=stocks.GroupBy(x=>x.ErpStockCode,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>x.First(),StringComparer.OrdinalIgnoreCase);

        var warehouseCodes=new[]{workOrder.IssueWarehouseCode,workOrder.WarehouseCode}.Distinct().ToArray();
        var warehouses=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>x.BranchCode==branch&&warehouseCodes.Contains(x.WarehouseCode))
            .ToListAsync(ct);
        var warehouseMap=warehouses.ToDictionary(x=>x.WarehouseCode);

        var configurationCodes=recipe.Select(x=>x.ComponentConfigurationCode)
            .Append(workOrder.ConfigurationCode).Where(x=>!string.IsNullOrWhiteSpace(x))
            .Select(x=>x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var yapCodes=configurationCodes.Length==0?[]:await uow.Repository<YapCodeEntity>().Query()
            .Where(x=>x.BranchCode==branch&&configurationCodes.Contains(x.ConfigurationCode))
            .ToListAsync(ct);
        long? ResolveYap(string? code,long? stockId)=>string.IsNullOrWhiteSpace(code)?null:yapCodes
            .Where(x=>string.Equals(x.ConfigurationCode,code.Trim(),StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x=>x.StockId==stockId).ThenBy(x=>x.Id).Select(x=>(long?)x.Id).FirstOrDefault();

        stockMap.TryGetValue(workOrder.StockCode,out var productStock);
        warehouseMap.TryGetValue(workOrder.IssueWarehouseCode,out var sourceWarehouse);
        warehouseMap.TryGetValue(workOrder.WarehouseCode,out var targetWarehouse);
        var errors=new List<string>();
        if(productStock is null)errors.Add($"Mamul stok WMS ERP aynasında bulunamadı: {workOrder.StockCode}");
        if(sourceWarehouse is null)errors.Add($"Çıkış deposu WMS ERP aynasında bulunamadı: {workOrder.IssueWarehouseCode}");
        if(targetWarehouse is null)errors.Add($"Üretim deposu WMS ERP aynasında bulunamadı: {workOrder.WarehouseCode}");

        var materials=recipe.Select(row=>
        {
            stockMap.TryGetValue(row.ComponentStockCode,out var stock);
            var error=stock is null?$"Bileşen stok WMS ERP aynasında bulunamadı: {row.ComponentStockCode}":null;
            if(error is not null)errors.Add(error);
            return new PreparedNetsisProductionMaterial(
                stock?.Id,row.ComponentStockCode,row.ComponentStockName,
                stock?.BaseUnitCode??row.ComponentUnitCode??"ADET",
                ResolveYap(row.ComponentConfigurationCode,stock?.Id),row.ComponentConfigurationCode,
                row.OperationNumber,row.RecipeQuantity,row.VariableWasteQuantity+row.FixedWasteQuantity,
                row.TotalRequiredQuantity,error);
        }).ToArray();

        var existing=await uow.Repository<ProductionOrder>().Query()
            .Where(x=>x.BranchCode==branch&&x.ExternalOrderNo==externalNo)
            .OrderByDescending(x=>x.Id)
            .Select(x=>new{x.Id,x.ProductionHeaderId,x.Header.DocumentNo}).FirstOrDefaultAsync(ct);
        return new PreparedNetsisProductionWorkOrder(
            workOrder.WorkOrderNumber,workOrder.BranchCode,workOrder.StockCode,workOrder.StockName,
            productStock?.BaseUnitCode??workOrder.UnitCode??"ADET",workOrder.WorkOrderQuantity,
            productStock?.Id,ResolveYap(workOrder.ConfigurationCode,productStock?.Id),workOrder.ConfigurationCode,
            sourceWarehouse?.Id,workOrder.IssueWarehouseCode,sourceWarehouse?.WarehouseName,
            targetWarehouse?.Id,workOrder.WarehouseCode,targetWarehouse?.WarehouseName,
            workOrder.WorkOrderDate,workOrder.DeliveryDate,workOrder.ProjectCode,workOrder.IsClosed,
            existing?.ProductionHeaderId,existing?.Id,existing?.DocumentNo,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),materials);
    }

    public Task<CreateProductionPlanResult> CreateAsync(
        CreateProductionPlanRequest request,
        long actor,
        CancellationToken ct=default)
    {
        ValidateEnvelope(request);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var existing=await Headers.Query()
                .Where(x=>x.CorrelationId==request.IdempotencyKey)
                .Select(x=>new CreateProductionPlanResult(
                    x.Id,x.DocumentNo,x.Orders.Count,
                    x.Orders.SelectMany(o=>o.Materials).Count(),
                    x.Orders.SelectMany(o=>o.Outputs).Count(),true))
                .SingleOrDefaultAsync(token);
            if(existing is not null)return existing;

            var branch=request.BranchCode.Trim();
            var stockIds=request.Orders.SelectMany(x=>
                    new[]{x.ProducedStockId}
                        .Concat((x.Materials??[]).Select(m=>m.StockId))
                        .Concat((x.Outputs??[]).Select(o=>o.StockId)))
                .Distinct().ToArray();
            var stocks=await uow.Repository<StockEntity>().Query()
                .Where(x=>stockIds.Contains(x.Id)&&x.BranchCode==branch)
                .ToDictionaryAsync(x=>x.Id,token);
            if(stocks.Count!=stockIds.Length)
                throw AppException.BadRequest("Seçilen üretim stoklarından biri ERP mirror tablosunda bulunamadı.");

            var warehouseIds=request.Orders.SelectMany(x=>
                    new[]{x.SourceWarehouseId,x.TargetWarehouseId}
                        .Concat((x.Materials??[]).Select(m=>m.SourceWarehouseId))
                        .Concat((x.Outputs??[]).Select(o=>o.TargetWarehouseId)))
                .Distinct().ToArray();
            var warehouses=await uow.Repository<WarehouseEntity>().Query()
                .Where(x=>warehouseIds.Contains(x.Id)&&x.BranchCode==branch)
                .ToDictionaryAsync(x=>x.Id,token);
            if(warehouses.Count!=warehouseIds.Length)
                throw AppException.BadRequest("Seçilen üretim depolarından biri ERP mirror tablosunda bulunamadı.");

            var yapIds=request.Orders.SelectMany(x=>
                    new long?[]{x.ProducedYapCodeId}
                        .Concat((x.Materials??[]).Select(m=>m.YapCodeId))
                        .Concat((x.Outputs??[]).Select(o=>o.YapCodeId)))
                .Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToArray();
            var yaps=await uow.Repository<YapCodeEntity>().Query()
                .Where(x=>yapIds.Contains(x.Id)&&x.BranchCode==branch)
                .ToDictionaryAsync(x=>x.Id,token);
            if(yaps.Count!=yapIds.Length)
                throw AppException.BadRequest("Seçilen yapılandırma kodlarından biri ERP mirror tablosunda bulunamadı.");
            ValidateYapStocks(request,yaps);

            var userIds=request.Orders.SelectMany(x=>x.AssignedUserIds??[]).Distinct().ToArray();
            if(userIds.Length>0)
            {
                var activeUsers=await uow.Repository<User>().Query()
                    .CountAsync(x=>userIds.Contains(x.Id)&&x.IsActive,token);
                if(activeUsers!=userIds.Length)
                    throw AppException.BadRequest("Atanan kullanıcılardan biri bulunamadı veya aktif değil.");
            }

            var locationPairs=request.Orders.SelectMany(x=>
                    (x.Materials??[]).Where(m=>m.PreferredSourceLocationId.HasValue)
                        .Select(m=>(Id:m.PreferredSourceLocationId!.Value,WarehouseId:m.SourceWarehouseId))
                        .Concat((x.Outputs??[]).Where(o=>o.PreferredTargetLocationId.HasValue)
                            .Select(o=>(Id:o.PreferredTargetLocationId!.Value,WarehouseId:o.TargetWarehouseId))))
                .ToArray();
            if(locationPairs.Length>0)
            {
                var locationIds=locationPairs.Select(x=>x.Id).Distinct().ToArray();
                var locations=await uow.Repository<WarehouseLocation>().Query()
                    .Where(x=>locationIds.Contains(x.Id)&&x.IsActive)
                    .ToDictionaryAsync(x=>x.Id,token);
                if(locations.Count!=locationIds.Length||
                   locationPairs.Any(x=>locations[x.Id].WarehouseId!=x.WarehouseId))
                    throw AppException.BadRequest("Tercih edilen üretim raflarından biri aktif değil veya seçilen depoya ait değil.");
            }

            CustomerEntity? customer=null;
            if(request.CustomerId.HasValue)
                customer=await uow.Repository<CustomerEntity>().Query()
                    .SingleOrDefaultAsync(x=>x.Id==request.CustomerId&&x.BranchCode==branch,token)
                    ??throw AppException.BadRequest("Seçilen müşteri ERP mirror tablosunda bulunamadı.");

            var tracking=new Dictionary<long,EffectiveStockTrackingPolicy>();
            foreach(var stockId in stockIds)
                tracking[stockId]=await trackingPolicyResolver.ResolveAsync(branch,stockId,token);

            var allocated=await numberAllocator.AllocateAsync(
                request.DocumentSeriesId,WmsDocumentType.ProductionOrder,DateTime.UtcNow,token);
            var now=DateTime.UtcNow;
            var header=new ProductionHeader
            {
                BranchCode=branch,CreatedBy=actor,CreatedDate=now,
                DocumentSeriesId=allocated.DocumentSeriesId,DocumentNo=allocated.DocumentNumber,
                DocumentDate=request.DocumentDate,CorrelationId=request.IdempotencyKey,
                PlanType=request.PlanType,ExecutionMode=request.ExecutionMode,
                Status=ProductionPlanStatus.Draft,Priority=request.Priority,
                CustomerId=customer?.Id,CustomerCodeSnapshot=customer?.CustomerCode,
                CustomerNameSnapshot=customer?.CustomerName,
                PlannedStartAtUtc=request.PlannedStartAtUtc?.ToUniversalTime(),
                PlannedEndAtUtc=request.PlannedEndAtUtc?.ToUniversalTime(),
                Description=Clean(request.Description,2000)
            };
            var orderMap=new Dictionary<string,ProductionOrder>(StringComparer.OrdinalIgnoreCase);
            var orderLine=0;
            foreach(var item in request.Orders.OrderBy(x=>x.SequenceNo))
            {
                var stock=stocks[item.ProducedStockId];
                var producedYap=item.ProducedYapCodeId.HasValue?yaps[item.ProducedYapCodeId.Value]:null;
                var lineNo=++orderLine;
                var order=new ProductionOrder
                {
                    BranchCode=branch,CreatedBy=actor,CreatedDate=now,Header=header,LineNo=lineNo,
                    OrderNo=$"{allocated.DocumentNumber}-O{lineNo:00}",ExternalOrderNo=Clean(item.ExternalOrderNo,100),
                    Status=ProductionOrderStatus.Draft,SequenceNo=item.SequenceNo,ParallelGroupNo=item.ParallelGroupNo,
                    BomReference=Clean(item.BomReference,100),RoutingReference=Clean(item.RoutingReference,100),
                    WorkCenterCode=Clean(item.WorkCenterCode,100),ProducedStockId=stock.Id,
                    ProducedStockCodeSnapshot=stock.ErpStockCode,ProducedStockNameSnapshot=stock.StockName,
                    ProducedYapCodeId=producedYap?.Id,ProducedYapCodeSnapshot=producedYap?.ConfigurationCode,
                    UnitCode=StockUnitPolicy.Resolve(stock,null),PlannedQuantity=item.PlannedQuantity,
                    SourceWarehouseId=item.SourceWarehouseId,TargetWarehouseId=item.TargetWarehouseId,
                    RequireMaterialTransferBeforeStart=item.RequireMaterialTransferBeforeStart,
                    PlannedStartAtUtc=item.PlannedStartAtUtc?.ToUniversalTime(),
                    PlannedEndAtUtc=item.PlannedEndAtUtc?.ToUniversalTime(),
                    Description=Clean(item.Description,1000)
                };
                var materialLine=0;
                foreach(var material in item.Materials??[])
                {
                    var materialStock=stocks[material.StockId];
                    var materialYap=material.YapCodeId.HasValue?yaps[material.YapCodeId.Value]:null;
                    order.Materials.Add(new ProductionMaterialRequirement
                    {
                        BranchCode=branch,CreatedBy=actor,CreatedDate=now,LineNo=++materialLine,
                        StockId=materialStock.Id,StockCodeSnapshot=materialStock.ErpStockCode,
                        StockNameSnapshot=materialStock.StockName,YapCodeId=materialYap?.Id,
                        YapCodeSnapshot=materialYap?.ConfigurationCode,
                        UnitCode=StockUnitPolicy.Resolve(materialStock,null),
                        RequiredQuantity=material.RequiredQuantity,IssueMode=material.IssueMode,
                        IsMandatory=material.IsMandatory,SourceWarehouseId=material.SourceWarehouseId,
                        PreferredSourceLocationId=material.PreferredSourceLocationId,
                        TrackingType=tracking[material.StockId].TrackingType
                    });
                }
                var outputLine=0;
                foreach(var output in item.Outputs??[])
                {
                    var outputStock=stocks[output.StockId];
                    var outputYap=output.YapCodeId.HasValue?yaps[output.YapCodeId.Value]:null;
                    order.Outputs.Add(new ProductionOutputExpectation
                    {
                        BranchCode=branch,CreatedBy=actor,CreatedDate=now,LineNo=++outputLine,
                        StockId=outputStock.Id,StockCodeSnapshot=outputStock.ErpStockCode,
                        StockNameSnapshot=outputStock.StockName,YapCodeId=outputYap?.Id,
                        YapCodeSnapshot=outputYap?.ConfigurationCode,
                        UnitCode=StockUnitPolicy.Resolve(outputStock,null),
                        PlannedQuantity=output.PlannedQuantity,TargetWarehouseId=output.TargetWarehouseId,
                        PreferredTargetLocationId=output.PreferredTargetLocationId,
                        TrackingType=tracking[output.StockId].TrackingType,IsPrimary=output.IsPrimary
                    });
                }
                var assignees=(item.AssignedUserIds??[]).Distinct().ToArray();
                foreach(var userId in assignees)
                    order.Assignments.Add(new ProductionOrderAssignment
                    {
                        BranchCode=branch,CreatedBy=actor,CreatedDate=now,UserId=userId,
                        IsPrimary=userId==assignees[0],AssignedAtUtc=DateTimeOffset.UtcNow,AssignedBy=actor
                    });
                header.Orders.Add(order);
                orderMap[item.LocalKey.Trim()]=order;
            }
            foreach(var dependency in request.Dependencies??[])
                header.Dependencies.Add(new ProductionOrderDependency
                {
                    BranchCode=branch,CreatedBy=actor,CreatedDate=now,
                    PredecessorOrder=orderMap[dependency.PredecessorOrderLocalKey.Trim()],
                    SuccessorOrder=orderMap[dependency.SuccessorOrderLocalKey.Trim()],
                    DependencyType=dependency.DependencyType,LagMinutes=dependency.LagMinutes,
                    RequireOutputAvailable=dependency.RequireOutputAvailable,
                    RequireTransferCompleted=dependency.RequireTransferCompleted
                });

            await Headers.AddAsync(header,token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "production.plan.create",nameof(ProductionHeader),header.Id.ToString(),
                "Succeeded","production",
                NewValues:new{header.DocumentNo,header.PlanType,header.ExecutionMode,OrderCount=header.Orders.Count},
                ChangedFields:["Header","Orders","Materials","Outputs","Assignments","Dependencies"]),token);
            return new(header.Id,header.DocumentNo,header.Orders.Count,
                header.Orders.Sum(x=>x.Materials.Count),header.Orders.Sum(x=>x.Outputs.Count),false);
        },ct,IsolationLevel.Serializable);
    }

    public async Task<PagedResponse<ProductionPlanGridRow>> GetPagedAsync(
        PagedRequest request,
        CancellationToken ct=default)
    {
        var orders=uow.Repository<ProductionOrder>().Query();
        var materials=uow.Repository<ProductionMaterialRequirement>().Query();
        var outputs=uow.Repository<ProductionOutputExpectation>().Query();
        var query=Headers.Query().Select(h=>new ProductionPlanGridRow(
            h.Id,h.BranchCode,h.DocumentNo,h.DocumentDate,h.PlanType,h.ExecutionMode,h.Status,h.Priority,
            h.CustomerCodeSnapshot,h.CustomerNameSnapshot,
            orders.Count(x=>x.ProductionHeaderId==h.Id),
            materials.Count(x=>x.Order.ProductionHeaderId==h.Id),
            outputs.Count(x=>x.Order.ProductionHeaderId==h.Id),
            orders.Where(x=>x.ProductionHeaderId==h.Id).Sum(x=>(decimal?)x.PlannedQuantity)??0,
            orders.Where(x=>x.ProductionHeaderId==h.Id).Sum(x=>(decimal?)x.CompletedQuantity)??0,
            h.PlannedStartAtUtc,h.PlannedEndAtUtc,h.CreatedBy,h.CreatedDate,h.UpdatedBy,h.UpdatedDate));
        if(!string.IsNullOrWhiteSpace(request.Search))
        {
            var search=request.Search.Trim();
            query=query.Where(x=>x.DocumentNo.Contains(search)||
                (x.CustomerCode!=null&&x.CustomerCode.Contains(search))||
                (x.CustomerName!=null&&x.CustomerName.Contains(search)));
        }
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request,nameof(ProductionPlanGridRow.CreatedDate))
            .ToPagedResponseAsync(request,ct);
    }

    public async Task<ProductionPlanDetail> GetDetailAsync(long id,CancellationToken ct=default)
    {
        var header=await Headers.Query().SingleOrDefaultAsync(x=>x.Id==id,ct)
            ??throw AppException.NotFound("Üretim planı bulunamadı.");
        var orders=await uow.Repository<ProductionOrder>().Query()
            .Where(x=>x.ProductionHeaderId==id).OrderBy(x=>x.SequenceNo).ThenBy(x=>x.LineNo)
            .ToListAsync(ct);
        var orderIds=orders.Select(x=>x.Id).ToArray();
        var materialRows=await uow.Repository<ProductionMaterialRequirement>().Query()
            .Where(x=>orderIds.Contains(x.ProductionOrderId)).OrderBy(x=>x.LineNo).ToListAsync(ct);
        var outputRows=await uow.Repository<ProductionOutputExpectation>().Query()
            .Where(x=>orderIds.Contains(x.ProductionOrderId)).OrderBy(x=>x.LineNo).ToListAsync(ct);
        var assignments=await uow.Repository<ProductionOrderAssignment>().Query()
            .Where(x=>orderIds.Contains(x.ProductionOrderId)).OrderByDescending(x=>x.IsPrimary).ThenBy(x=>x.Id)
            .ToListAsync(ct);
        var users=await uow.Repository<User>().Query()
            .Where(x=>assignments.Select(a=>a.UserId).Contains(x.Id))
            .Include(x=>x.Detail).ToDictionaryAsync(x=>x.Id,ct);
        var dependencies=await uow.Repository<ProductionOrderDependency>().Query()
            .Where(x=>x.ProductionHeaderId==id).OrderBy(x=>x.Id)
            .Select(x=>new ProductionDependencyDto(x.Id,x.PredecessorOrderId,x.SuccessorOrderId,
                x.DependencyType,x.LagMinutes,x.RequireOutputAvailable,x.RequireTransferCompleted))
            .ToListAsync(ct);
        var orderDtos=orders.Select(order=>new ProductionOrderDto(
            order.Id,order.LineNo,order.OrderNo,order.ExternalOrderNo,order.Status,order.SequenceNo,
            order.ParallelGroupNo,order.BomReference,order.RoutingReference,order.WorkCenterCode,
            order.ProducedStockId,order.ProducedStockCodeSnapshot,order.ProducedStockNameSnapshot,
            order.ProducedYapCodeId,order.ProducedYapCodeSnapshot,order.UnitCode,order.PlannedQuantity,
            order.CompletedQuantity,order.ScrapQuantity,order.SourceWarehouseId,order.TargetWarehouseId,
            order.RequireMaterialTransferBeforeStart,order.PlannedStartAtUtc,order.PlannedEndAtUtc,
            materialRows.Where(x=>x.ProductionOrderId==order.Id).Select(x=>new ProductionMaterialDto(
                x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,
                x.YapCodeSnapshot,x.UnitCode,x.RequiredQuantity,x.IssuedQuantity,x.ConsumedQuantity,
                x.IssueMode,x.IsMandatory,x.SourceWarehouseId,x.PreferredSourceLocationId,x.TrackingType)).ToArray(),
            outputRows.Where(x=>x.ProductionOrderId==order.Id).Select(x=>new ProductionOutputDto(
                x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,
                x.YapCodeSnapshot,x.UnitCode,x.PlannedQuantity,x.ProducedQuantity,x.ScrapQuantity,
                x.TargetWarehouseId,x.PreferredTargetLocationId,x.TrackingType,x.IsPrimary)).ToArray(),
            assignments.Where(x=>x.ProductionOrderId==order.Id).Select(x=>
            {
                var user=users[x.UserId];
                var display=string.Join(' ',new[]{user.Detail?.FirstName,user.Detail?.LastName}
                    .Where(value=>!string.IsNullOrWhiteSpace(value)));
                return new ProductionAssignmentDto(x.Id,x.UserId,user.Username,
                    string.IsNullOrWhiteSpace(display)?user.Username:display,x.IsPrimary,x.AssignedAtUtc,
                    x.AcceptedAtUtc,x.CompletedAtUtc,x.Note);
            }).ToArray())).ToArray();
        var row=new ProductionPlanGridRow(
            header.Id,header.BranchCode,header.DocumentNo,header.DocumentDate,header.PlanType,
            header.ExecutionMode,header.Status,header.Priority,header.CustomerCodeSnapshot,
            header.CustomerNameSnapshot,orders.Count,materialRows.Count,outputRows.Count,
            orders.Sum(x=>x.PlannedQuantity),orders.Sum(x=>x.CompletedQuantity),
            header.PlannedStartAtUtc,header.PlannedEndAtUtc,header.CreatedBy,header.CreatedDate,
            header.UpdatedBy,header.UpdatedDate);
        return new(row,Convert.ToBase64String(header.RowVersion),header.Description,orderDtos,dependencies);
    }

    public Task<ProductionPlanDetail> ReleaseAsync(
        long id,
        ProductionTransitionRequest request,
        long actor,
        CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>
        {
            var header=await Headers.Query(tracking:true).Include(x=>x.Orders).ThenInclude(x=>x.Materials)
                .Include(x=>x.Orders).ThenInclude(x=>x.Outputs)
                .Include(x=>x.Orders).ThenInclude(x=>x.Assignments)
                .SingleOrDefaultAsync(x=>x.Id==id,token)
                ??throw AppException.NotFound("Üretim planı bulunamadı.");
            if(header.Status!=ProductionPlanStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak üretim planı serbest bırakılabilir.");
            EnsureRowVersion(header.RowVersion,request.RowVersion);
            if(header.Orders.Count==0||header.Orders.Any(x=>x.Outputs.Count==0))
                throw AppException.Conflict("Her üretim emrinin en az bir çıktı kalemi bulunmalıdır.");
            if(header.Orders.Any(x=>x.Assignments.Count==0&&string.IsNullOrWhiteSpace(x.WorkCenterCode)))
                throw AppException.Conflict("Her üretim emri kullanıcıya atanmalı veya bir iş merkezine bağlanmalıdır.");
            if(header.Orders.Any(x=>x.RequireMaterialTransferBeforeStart&&x.Materials.Count==0))
                throw AppException.Conflict("Malzeme transferi zorunlu emirlerde en az bir malzeme ihtiyacı bulunmalıdır.");
            var now=DateTimeOffset.UtcNow;
            header.Status=ProductionPlanStatus.Released;header.ReleasedAtUtc=now;header.ReleasedBy=actor;
            header.UpdatedBy=actor;header.UpdatedDate=now.UtcDateTime;
            foreach(var order in header.Orders)
            {
                order.Status=ProductionOrderStatus.Released;order.UpdatedBy=actor;order.UpdatedDate=now.UtcDateTime;
            }
            try{await uow.SaveChangesAsync(token);}
            catch(DbUpdateConcurrencyException){throw AppException.Conflict("Üretim planı başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");}
            await audit.WriteAsync(new(
                "production.plan.release",nameof(ProductionHeader),id.ToString(),
                "Succeeded","production",NewValues:new{header.Status,header.ReleasedAtUtc,Reason=Clean(request.Reason,500)},
                ChangedFields:["Status","Orders"]),token);
            return await GetDetailAsync(id,token);
        },ct);

    public Task DeleteDraftAsync(long id,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>
        {
            var header=await Headers.Query(tracking:true).SingleOrDefaultAsync(x=>x.Id==id,token)
                ??throw AppException.NotFound("Üretim planı bulunamadı.");
            if(header.Status!=ProductionPlanStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak üretim planı silinebilir.");
            var orderIds=await uow.Repository<ProductionOrder>().Query()
                .Where(x=>x.ProductionHeaderId==id).Select(x=>x.Id).ToArrayAsync(token);
            var now=DateTime.UtcNow;
            await SoftDelete(uow.Repository<ProductionMaterialRequirement>().Query(),x=>orderIds.Contains(x.ProductionOrderId),actor,now,token);
            await SoftDelete(uow.Repository<ProductionOutputExpectation>().Query(),x=>orderIds.Contains(x.ProductionOrderId),actor,now,token);
            await SoftDelete(uow.Repository<ProductionOrderAssignment>().Query(),x=>orderIds.Contains(x.ProductionOrderId),actor,now,token);
            await SoftDelete(uow.Repository<ProductionOrderDependency>().Query(),x=>x.ProductionHeaderId==id,actor,now,token);
            await SoftDelete(uow.Repository<ProductionOrder>().Query(),x=>x.ProductionHeaderId==id,actor,now,token);
            header.IsDeleted=true;header.DeletedBy=actor;header.DeletedDate=now;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "production.plan.delete",nameof(ProductionHeader),id.ToString(),
                "Succeeded","production",OldValues:new{header.DocumentNo,header.Status},ChangedFields:["IsDeleted"]),token);
            return true;
        },ct);

    private static void ValidateEnvelope(CreateProductionPlanRequest r)
    {
        if(r.IdempotencyKey==Guid.Empty)throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        if(string.IsNullOrWhiteSpace(r.BranchCode))throw AppException.BadRequest("Şube kodu zorunludur.");
        if(r.DocumentSeriesId<=0)throw AppException.BadRequest("Üretim belge serisi zorunludur.");
        if(r.Priority is <1 or >9)throw AppException.BadRequest("Öncelik 1-9 arasında olmalıdır.");
        if(r.Orders.Count is <1 or >100)throw AppException.BadRequest("Üretim planında 1-100 emir bulunmalıdır.");
        if(r.PlannedStartAtUtc.HasValue&&r.PlannedEndAtUtc.HasValue&&r.PlannedEndAtUtc<r.PlannedStartAtUtc)
            throw AppException.BadRequest("Plan bitiş zamanı başlangıç zamanından önce olamaz.");
        var keys=r.Orders.Select(x=>x.LocalKey?.Trim()).ToArray();
        if(keys.Any(string.IsNullOrWhiteSpace)||keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=keys.Length)
            throw AppException.BadRequest("Üretim emirlerinin yerel anahtarları zorunlu ve tekil olmalıdır.");
        foreach(var order in r.Orders)
        {
            if(order.ProducedStockId<=0||order.PlannedQuantity<=0||order.SourceWarehouseId<=0||order.TargetWarehouseId<=0)
                throw AppException.BadRequest("Üretim emrinde üretilen stok, pozitif miktar, kaynak ve hedef depo zorunludur.");
            if(order.SequenceNo<=0)throw AppException.BadRequest("Üretim emri sıra numarası pozitif olmalıdır.");
            if(order.PlannedStartAtUtc.HasValue&&order.PlannedEndAtUtc.HasValue&&order.PlannedEndAtUtc<order.PlannedStartAtUtc)
                throw AppException.BadRequest("Üretim emri bitiş zamanı başlangıç zamanından önce olamaz.");
            var materials=order.Materials??[];
            if(materials.Any(x=>x.StockId<=0||x.RequiredQuantity<=0||x.SourceWarehouseId<=0))
                throw AppException.BadRequest("Malzeme ihtiyaçlarında stok, pozitif miktar ve kaynak depo zorunludur.");
            if(materials.GroupBy(x=>new{x.StockId,x.YapCodeId,x.SourceWarehouseId}).Any(x=>x.Count()>1))
                throw AppException.BadRequest("Aynı stok/yapılandırma/depo malzeme ihtiyacı bir emirde tekrarlanamaz.");
            var outputs=order.Outputs??[];
            if(outputs.Count==0||outputs.Any(x=>x.StockId<=0||x.PlannedQuantity<=0||x.TargetWarehouseId<=0))
                throw AppException.BadRequest("Her üretim emrinde en az bir geçerli çıktı kalemi bulunmalıdır.");
            if(outputs.Count(x=>x.IsPrimary)!=1)
                throw AppException.BadRequest("Her üretim emrinde tam bir ana çıktı bulunmalıdır.");
            var primary=outputs.Single(x=>x.IsPrimary);
            if(primary.StockId!=order.ProducedStockId||primary.YapCodeId!=order.ProducedYapCodeId)
                throw AppException.BadRequest("Ana çıktı, üretim emrinin üretilen stok ve yapılandırma koduyla eşleşmelidir.");
            if(primary.PlannedQuantity!=order.PlannedQuantity)
                throw AppException.BadRequest("Ana çıktı miktarı üretim emri planlanan miktarıyla eşleşmelidir.");
        }
        ValidateDependencies(keys!,r.Dependencies??[]);
    }

    private static void ValidateDependencies(
        IReadOnlyCollection<string> orderKeys,
        IReadOnlyCollection<ProductionDependencyDraftRequest> dependencies)
    {
        var keySet=orderKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(dependencies.Any(x=>!keySet.Contains(x.PredecessorOrderLocalKey.Trim())||
                               !keySet.Contains(x.SuccessorOrderLocalKey.Trim())))
            throw AppException.BadRequest("Üretim bağımlılıklarından biri bilinmeyen bir emre bağlıdır.");
        if(dependencies.Any(x=>string.Equals(x.PredecessorOrderLocalKey.Trim(),x.SuccessorOrderLocalKey.Trim(),StringComparison.OrdinalIgnoreCase)))
            throw AppException.BadRequest("Üretim emri kendisine bağımlı olamaz.");
        if(dependencies.Any(x=>x.LagMinutes<0))
            throw AppException.BadRequest("Bağımlılık gecikme süresi negatif olamaz.");
        if(dependencies.GroupBy(x=>
                $"{x.PredecessorOrderLocalKey.Trim().ToUpperInvariant()}\u001F{x.SuccessorOrderLocalKey.Trim().ToUpperInvariant()}")
            .Any(x=>x.Count()>1))
            throw AppException.BadRequest("Aynı üretim emri bağımlılığı tekrarlanamaz.");
        var graph=keySet.ToDictionary(x=>x,_=>new List<string>(),StringComparer.OrdinalIgnoreCase);
        foreach(var dependency in dependencies)
            graph[dependency.PredecessorOrderLocalKey.Trim()].Add(dependency.SuccessorOrderLocalKey.Trim());
        var visiting=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Cycle(string node)
        {
            if(visiting.Contains(node))return true;
            if(!visited.Add(node))return false;
            visiting.Add(node);
            foreach(var next in graph[node])if(Cycle(next))return true;
            visiting.Remove(node);
            return false;
        }
        if(graph.Keys.Any(Cycle))throw AppException.BadRequest("Üretim emri bağımlılıklarında döngü bulunamaz.");
    }

    private static void ValidateYapStocks(
        CreateProductionPlanRequest request,
        IReadOnlyDictionary<long,YapCodeEntity> yaps)
    {
        foreach(var pair in request.Orders.SelectMany(x=>
                     new[]{(StockId:x.ProducedStockId,YapId:x.ProducedYapCodeId)}
                         .Concat((x.Materials??[]).Select(m=>(StockId:m.StockId,YapId:m.YapCodeId)))
                         .Concat((x.Outputs??[]).Select(o=>(StockId:o.StockId,YapId:o.YapCodeId)))))
            if(pair.YapId.HasValue&&yaps[pair.YapId.Value].StockId.HasValue&&
               yaps[pair.YapId.Value].StockId!=pair.StockId)
                throw AppException.BadRequest("Yapılandırma kodu seçilen stokla eşleşmiyor.");
    }

    private static void EnsureRowVersion(byte[] current,string supplied)
    {
        byte[] expected;
        try{expected=Convert.FromBase64String(supplied??string.Empty);}
        catch(FormatException){throw AppException.BadRequest("Geçersiz eşzamanlılık anahtarı.");}
        if(!CryptographicOperations.FixedTimeEquals(current,expected))
            throw AppException.Conflict("Üretim planı başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
    }

    private static string? Clean(string? value,int max)
    {
        var clean=value?.Trim();
        if(string.IsNullOrEmpty(clean))return null;
        return clean.Length<=max?clean:clean[..max];
    }

    private static Task SoftDelete<TEntity>(
        IQueryable<TEntity> query,
        System.Linq.Expressions.Expression<Func<TEntity,bool>> predicate,
        long actor,
        DateTime now,
        CancellationToken ct)
        where TEntity:verii_wms_api_v2.Shared.Domain.BaseEntity=>
        query.Where(predicate).ExecuteUpdateAsync(x=>x
            .SetProperty(v=>v.IsDeleted,true)
            .SetProperty(v=>v.DeletedBy,actor)
            .SetProperty(v=>v.DeletedDate,now),ct);

}
