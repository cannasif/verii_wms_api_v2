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

## Supplier quotation portal

RFQ participants can receive an expiring, revocable portal link by e-mail. The raw portal token is
returned only in the e-mail URL; the database stores its SHA-256 hash. A supplier can open the RFQ,
save unit prices and delivery dates as a draft, and submit the quotation without a WMS user account.
Submission freezes that revision. Internal users can approve/reject it or request a new revision; the
previous quotation remains traceable and a new linked draft is created. Re-sending an invitation
rotates the token so the earlier URL immediately becomes unusable.

Public portal endpoints are anonymous by design, rate limited, token scoped and do not expose internal
database identifiers beyond RFQ line identifiers required by the signed workflow. SMTP delivery uses
the existing centrally managed WMS SMTP configuration.
