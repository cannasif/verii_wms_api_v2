# NetsisRead

ERP okuma uçları bu feature altında tutulacak. Her sorgu `Application/Dtos` ile dış sözleşmeye dönüştürülür; entity modelleri API’ye sızdırılmaz.

Planlanan alanlar: Items, ARPs, Warehouses, Branches, ProjectCodes, SpecialCodes, ExchangeRates, StockGroups.

## Stock balances

`GET /api/netsis-read/stock-balances` reads `dbo.RII_FN_STOCK_BALANCE` without EF tracking.
Both filters are optional:

- `warehouseCode`: Netsis warehouse code.
- `stockCode`: exact Netsis stock code.

Omitting both filters executes `dbo.RII_FN_STOCK_BALANCE(NULL, NULL)` and returns all rows.
