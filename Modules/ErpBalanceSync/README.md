# ERP stock balance authority synchronization

`RII_FN_STOCK_BALANCE` is treated as the authoritative ERP warehouse/stock total.

- `RII_ERP_WAREHOUSE_STOCK_BALANCE` stores the latest ERP total, the WMS movement projection total and their difference.
- `RII_ERP_STOCK_BALANCE_CHANGE_LOG` records only meaningful changes such as `WMS=5, ERP=7, difference=2`.
- `RII_ERP_STOCK_BALANCE_SYNC_RUN` records full/targeted runs, duration, counts and failures.
- The recurring Hangfire job runs every five minutes and has `AutomaticRetry(Attempts = 0)`.
- A successful WMS-to-Netsis document posts one targeted job containing the affected warehouse/stock pairs.
- A failed targeted enqueue never changes a successful ERP posting to failed; the next full run is the safety net.

Paged audit endpoints:

- `POST /api/erp-balance-sync/balances/paged`
- `POST /api/erp-balance-sync/changes/paged`
- `POST /api/erp-balance-sync/runs/paged`
- `POST /api/erp-balance-sync/sync/full`
- `POST /api/erp-balance-sync/sync/targeted`

## Locking and consistency

The full source is read into a SQL Server temporary table. There is no explicit transaction around the complete snapshot.
Only new or changed authority rows are written, in short configurable batches with indexed source keys. This prevents
100,000 unchanged rows from being updated every five minutes and avoids holding one transaction across the complete run.

The ERP result does not contain rack, lot, serial, configuration or stock-status dimensions. For that reason this module
does not overwrite `RII_LOCATION_STOCK_BALANCE` or fabricate serial movements. Those operational projections continue to
come from the immutable WMS movement ledger. The authority table exposes the discrepancy for controlled investigation.

If a full source count suddenly falls below the configured minimum or below a configured ratio of the last successful
full run, synchronization fails before applying changes. This protects WMS from a temporary ERP/query outage appearing
as a mass zero balance.
