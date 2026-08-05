# Windbox üretim iş emri ve reçete entegrasyonu

Windbox, WMS operasyon tablolarına (`RII_PR_ORDER`, `RII_PR_MATERIAL`, transfer ve stok hareketi tabloları) doğrudan kayıt atmaz. Entegrasyon sınırı aşağıdaki iki sürümlü kaynak tablodur:

- `RII_PR_SOURCE_ORDER`: iş emri başlığı ve mamul bilgisi
- `RII_PR_SOURCE_RECIPE`: iş emrine bağlı reçete bileşenleri

## Yazma sırası

1. İş emrini yeni bir `RevisionNumber` ile `Draft` durumunda ekleyin.
2. Aynı transaction içinde reçete satırlarını başlığın `Id` değeriyle ekleyin.
3. Miktar, depo, birim ve satır toplamlarını doğrulayın.
4. Başlığı son işlem olarak `Ready` durumuna çekin ve `SourceUpdatedAtUtc` değerini UTC yazın.
5. Daha sonraki değişikliklerde mevcut sürümü değiştirmek yerine yeni bir revizyon ekleyin.

WMS yalnızca `Ready` veya `Released` kayıtları okur ve aynı iş emrinin en yüksek revizyonunu kullanır. `Draft`, `OnHold`, `Closed` ve `Cancelled` kayıtlar operasyona alınmaz.

## Zorunlu başlık alanları

`BranchCode`, `SourceSystemCode`, `ExternalKey`, `WorkOrderNumber`, `RevisionNumber`, `Status`, `ProductCode`, `PlannedQuantity`, `UnitCode`, `SourceWarehouseCode`, `TargetWarehouseCode`, `SourceUpdatedAtUtc`.

`SourceSystemCode`, şube üretim transfer politikasındaki kaynak sistem koduyla eşleşmelidir; varsayılan değer `WINDBOX`'tır. `ExternalKey`, Windbox tarafındaki değişmeyen teknik kimliktir.

## Zorunlu reçete alanları

`ProductionSourceWorkOrderId`, `LineNumber`, `ComponentStockCode`, `UnitCode`, `RecipeQuantity`, `TotalRequiredQuantity`.

Her `(ProductionSourceWorkOrderId, LineNumber)` çifti tekildir. Stok ve depo kodları WMS ERP mirror tablolarıyla eşleşmeden üretim emri veya transfer oluşturulamaz.

## Veri sahipliği

- Windbox: kaynak iş emri, revizyon ve reçete içeriği.
- WMS: stok/depo eşleme doğrulaması, üretim planı, görev ataması, rezervasyon, raf/seri/lot seçimi, transfer, stok hareketi, audit ve iptal.
- ERP modu seçildiğinde bu tablolar okunmaz; mevcut Netsis read fonksiyonları kullanılır.

Windbox SQL kullanıcısına yalnızca bu iki tablo için gerekli `SELECT/INSERT/UPDATE` yetkileri verilmelidir. Şema değişikliği, silme ve diğer WMS tablolarına erişim verilmemelidir.
