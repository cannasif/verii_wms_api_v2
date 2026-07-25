# Goods Receipt V2 Blueprint

## Amaç

Eski WMS'in sahada doğrulanmış mal kabul akışını korumak; ekranlara dağılmış kuralları tek `GoodsReceipt` modülünde, ortak header/line/workflow/execution modeliyle yönetmek. Aynı çekirdek ileride transfer, sevk ve ambar işlemlerinde tekrar kullanılacaktır.

## Eski WMS ekran envanteri ve sıra

Eski web uygulamasında mal kabul için dokuz ana rota vardır:

1. `/goods-receipt/create`: siparişli veya siparişsiz emir başlangıcı.
2. `/goods-receipt/edit/:id`: taslak emir ve satır düzenleme.
3. `/goods-receipt/process`: görev/emir üzerinden fiziksel kabul.
4. `/goods-receipt/list`: tüm mal kabul belgelerinin izlenmesi.
5. `/goods-receipt/assigned`: kullanıcıya atanmış kabul işleri.
6. `/goods-receipt/approval`: fazla/eksik kabul ve süreç onay kuyruğu.
7. `/goods-receipt/pre-labels`: kabul öncesi etiket üretimi ve basımı.
8. `/goods-receipt/pre-label-receiving`: önceden basılmış etiketle kabul.
9. `/goods-receipt/collection/:headerId`: tek mal kabul emri için toplama/kabul çalışma ekranı.

Kullanıcı sırası tek bir doğrusal akış değildir. Dört başlangıç yolu aynı çekirdeğe bağlanır:

| Başlangıç | Kaynak | Görev | Fiziksel kabul | Kullanım |
|---|---|---:|---:|---|
| Siparişten emir | Netsis açık sipariş | Evet | Sonra | Planlı satınalma kabulü |
| Siparişsiz emir | Manuel irsaliye/stok | Evet | Sonra | Siparişsiz fakat planlı kabul |
| Siparişe doğrudan kabul | Netsis açık sipariş | Hayır | Aynı işlem | Hızlı kabul |
| Siparişsiz doğrudan kabul | Manuel irsaliye/stok | Hayır | Aynı işlem | Emirsiz saha kabulü |

Ön etiket, tedarikçi etiketi ve kabul anında etiket üretimi bu yolların alternatifi değil, her yol üzerinde uygulanabilen etiket stratejileridir.

## V2 tek çatı modeli

- `RII_GR_HEADER`: ticari/operasyonel mal kabul belgesi, tedarikçi ve irsaliye snapshot'ları, politika snapshot'ları ve ana durumlar.
- `RII_GR_LINE`: beklenen, alınan, kabul, ret ve karantina miktarları; stok/YAP/birim snapshot'ları.
- `RII_GR_SOURCE_DOCUMENT` ve line source: Netsis siparişi, irsaliye ve e-irsaliye bağlantıları.
- `RII_GR_TASK`, task line ve assignment: iş emri, kişi/kişiler, planlanan ve toplanan miktar.
- `RII_GR_EXECUTION` ve execution line: değiştirilemez fiziksel kabul kanıtı; idempotency, cihaz, barkod, lot, seri, tarih, lokasyon ve stok hareketi bağlantısı.
- Quality inspection: kabul satırından doğan kalite kararı ve karantina.
- Stock movement ledger: bakiyeyi değiştiren tek muhasebe kaynağı.
- Label batch/label: ön etiket, tedarikçi etiketi ve kabulde üretim.
- Status history: operasyon, onay, kalite, yerleştirme ve ERP durum geçmişi.

Tüm zaman damgaları UTC saklanır; web tarafında proje ayarındaki saat dilimine göre gösterilir. Belge tarihi ve irsaliye tarihi gibi iş tarihleri `DateOnly` olarak kalır.

## Durum ve miktar ilkeleri

- Fiziksel kabul kaydı silinmez veya güncellenmez; hata ters hareketle düzeltilir.
- Tekrar gönderim aynı idempotency anahtarı ve aynı içerikte replay döndürür; farklı içerik conflict üretir.
- Kalite bekleyen miktar politika gereği `QualityHold` statüsüne girer ve kullanılabilir bakiyeye karışmaz.
- Header/line toplamları execution kayıtlarının projection'ıdır; doğrudan elle bakiye güncellenmez.
- Fazla kabul, eksik kapama, kabul onayı, kalite onayı ve ERP aktarımı header'a kopyalanan politika snapshot'ından yürür. Sonradan ayar değişmesi açık belgenin geçmiş kuralını değiştirmez.
- Seri numaralı satırda bir execution satırı bir adet fiziksel birimi temsil eder.

## V2 ekran sınırları

Bu turda çalışan modül yüzeyleri:

- Süreç Merkezi
- Siparişten Emir
- Siparişsiz Emir
- Doğrudan Mal Kabul
- Mal Kabul Kayıtları ve detay modalı
- Süreç Ayarları
- Ayrı Kalite modülü: ayarlar, stok kuralları, kontrol kuyruğu

Takip eden uygulama dilimleri:

1. Atanan işler ve görev release/start/pause/complete state machine.
2. Barkod/ön etiketle fiziksel kabul çalışma ekranı.
3. Fazla/eksik kabul ve süreç onay kuyruğu.
4. Taslak düzenleme/iptal ve optimistic concurrency.
5. Kalite kararından stok statüsü dönüşümü ve ERP outbox/retry.

Bu ekranlar ayrı veri modelleri kurmamalı; mevcut header/line/task/execution/label ve quality tablolarını application command/query servisleri üzerinden kullanmalıdır.
