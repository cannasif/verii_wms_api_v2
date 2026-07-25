# Goods Receipt and Quality — Old WMS to V2 Gap Matrix

## Architecture decision

Continue with V2. The old WMS remains the behavioural reference, but its duplicated `TerminalLine` assignment pattern must not be copied. V2 uses `GoodsReceiptTask`, `GoodsReceiptTaskLine`, `GoodsReceiptTaskAssignment`, `GoodsReceiptExecution` and immutable stock movements.

## Goods receipt

| Capability | Old WMS | V2 status | Next acceptance criterion |
|---|---|---|---|
| Purchase-order task creation | Available | Complete | Multiple Netsis orders can create one receipt task and reserve exact open quantities |
| Orderless task | Available | Complete | Waybill validation, stock lines, assignee and idempotency |
| Direct orderless receipt | Available | Complete | Atomic receipt, quality hold, stock movement and audit |
| User assignment | Embedded in terminal lines | Complete with dedicated assignment aggregate | One or many active users; optimistic concurrency; reassignment blocked after start |
| My assigned orders | Available | Complete | Server-side paged/filterable/sortable queue; accept and start actions |
| Task management | Partial | Complete for assign/accept/start | Supervisor grid, detail and assignee management |
| Direct receipt from one or more purchase orders | Available | Planned | Select orders, receive immediately and commit reservation + execution atomically |
| Barcode collection | Available | Planned | Scan resolves task/line/label; idempotent quantity collection and serial uniqueness |
| Pause/resume/short close/complete | Available | Planned | Explicit state machine and policy-based approval gates |
| Pre-label workflow | Available | Domain ready | Generate, print, consume and void screens/services |
| ERP posting and retry | Available | Planned | Outbox, retry, commit-uncertain handling and audit |

## Quality

| Capability | Old WMS | V2 status | Notes |
|---|---|---|---|
| Global parameters | Available | Complete | Quality/put-away/ERP holds and default locations |
| Stock/group rules | Available | Complete | Sampling, required tracking data and fail action |
| Auto inspection from receipt | Available | Complete for direct receipt | Lot/serial/expiry snapshots retained |
| Inspection detail | Available | Complete | Quantity, sample, lot, serial and expiry visible |
| Quality decision | Available | Complete | Accept, quarantine, reject and supplier return |
| Physical disposition | Available | Complete | Immutable status/location movement; no direct balance mutation |
| Quarantine decision queue | Available | Complete | Release, reject or supplier return after quarantine |
| Partial line decisions | Available | Planned | Current endpoint decides all pending lines together |
| Test templates/results/AQL | Richer | Planned | Parameter/test definition, measured result, tolerance and attachments |
| Inspector assignment/priority | Partial | Planned | Dedicated quality work assignment |
| Manager release approval | Parameter exists | Planned | Approval workflow must enforce the parameter |

## Non-negotiable invariants

- Store operational timestamps in UTC and localize only at presentation boundaries.
- Never mutate balances directly; every quantity/status/location change is an immutable stock movement.
- Infer the current user from JWT for “my work” endpoints; never accept another user id from the client.
- Reserve order quantities under a serializable transaction and idempotency key.
- A started, completed or cancelled task cannot be silently reassigned.
- Quarantine is an intermediate disposition, not a final rejection.
- Lot and serial traceability must survive receipt, quality, put-away, transfer, shipment and return.
