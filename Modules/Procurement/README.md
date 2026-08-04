# Procurement module boundary

`Procurement` owns the commercial process before physical receipt:

1. Purchase request and approval
2. Request for quotation (RFQ) and invited suppliers
3. Supplier quotation and commercial decision
4. Purchase order and approval
5. Open order quantities offered to warehouse receiving

The module is intentionally independent from `GoodsReceipt`. Procurement tables never hold a
`GoodsReceiptId`, and receipt tables do not depend on procurement tables. The integration boundary is
`ProcurementReceiptSourceLine`, exposed through `GET /api/procurement/receipt-source/open-lines`.

When the two processes are connected, a Goods Receipt order-source adapter should consume this
contract and update `ReceivedQuantity` idempotently after physical receipt completion. This preserves
the existing Netsis order-source implementation and allows project-specific source selection without
changing either aggregate.

Supplier, stock, project and unit values are stored as document snapshots. Source identifiers are kept
for traceability, while commercial history remains readable if ERP mirror master data later changes.

All documents are branch-scoped, soft-deletable and audited through the existing WMS infrastructure.
State changes are explicit and recorded in `RII_PC_STATUS_HISTORY`.
