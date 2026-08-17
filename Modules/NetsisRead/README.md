# NetsisRead

ERP okuma uçları bu feature altında tutulacak. Her sorgu `Application/Dtos` ile dış sözleşmeye dönüştürülür; entity modelleri API’ye sızdırılmaz.

Planlanan alanlar: Items, ARPs, Warehouses, Branches, ProjectCodes, SpecialCodes, ExchangeRates, StockGroups.

## Stock balances

`GET /api/netsis-read/stock-balances` reads `dbo.RII_FN_STOCK_BALANCE` without EF tracking.
Both filters are optional:

- `warehouseCode`: Netsis warehouse code.
- `stockCode`: exact Netsis stock code.

Omitting both filters executes `dbo.RII_FN_STOCK_BALANCE(NULL, NULL)` and returns all rows.

## Import open files

`GET /api/netsis-read/imports/open-files` reads `dbo.RII_FN_ITHALAT_ACIK_DOSYALAR()`.
The response is a read-only ERP projection; it is not tracked as a WMS EF entity.
File and customer codes are required by the source schema, while customer names and
delivery-customer fields are nullable.
