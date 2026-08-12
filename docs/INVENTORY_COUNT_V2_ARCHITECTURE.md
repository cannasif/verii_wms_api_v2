# WMS v2 Sayım Mimarisi

## Amaç

Sayım modülü, fiziksel depodaki miktarı sistem bakiyesiyle güvenli biçimde karşılaştırır. Sayım sonucu doğrudan bakiye tablosuna yazılmaz. Onaylanan farklar, `RII_STOCK_MOVEMENT_OPERATION` ve `RII_STOCK_MOVEMENT_ENTRY` üzerinden değiştirilemez düzeltme hareketleri olarak işlenir; raf ve depo bakiyeleri mevcut projection servisi tarafından güncellenir.

## WMS v1 değerlendirmesi

WMS v1 şu yararlı kavramları içerir:

- Genel, depo, stok, raf, hücre ve karma kapsam
- Kör ve açık sayım
- Kullanıcı, rol veya ekip ataması
- İlk sayım ve yeniden sayım kayıtları
- Fark, fazla ve eksik stok işaretleri

Ancak v1'in üretim kullanımını engelleyen eksikleri vardır:

- Sayım satırları gerçek raf bakiyesi snapshot'ından üretilmez; beklenen miktar `0` başlatılır.
- Raf, stok, lot, seri, yapılandırma kodu ve stok statüsü aynı kanonik boyut anahtarıyla korunmaz.
- Barkod ve yanlış raf doğrulaması yoktur.
- Durumlar serbest metindir; geçersiz durum geçişleri engellenmez.
- Aynı lokasyonda iki aktif sayımı güvenli biçimde engelleyen kural yoktur.
- Bağımsız ikinci sayım, tolerans ve yetki bazlı fark onayı yoktur.
- Onaylanan farklar değiştirilemez stok hareketi ve ters kayıt zincirine bağlı değildir.
- Eş zamanlılık, idempotency, cihaz oturumu ve reddedilen okutma kanıtı yoktur.

## V2 temel ilkeleri

1. **Snapshot:** Emir serbest bırakıldığında `RII_LOCATION_STOCK_BALANCE` satırları ve son hareket kimliği sayım satırlarına kopyalanır.
2. **Kanonik boyut:** Depo + raf + stok + yapılandırma kodu + birim + lot + seri + stok statüsü birlikte değerlendirilir.
3. **Körlük:** Kör sayımda depo personeline beklenen miktar ve fark gösterilmez.
4. **Append-only kanıt:** Her başarılı veya reddedilmiş okutma ayrı olaydır; geçmiş kayıt değiştirilmez.
5. **İdempotency:** El terminalinin aynı isteği tekrar göndermesi ikinci bir sayım kaydı oluşturmaz.
6. **Ayrık görev:** Bir sayım emri birden fazla lokasyon görevine bölünür. Atama, başlatma ve tamamlama görev seviyesindedir.
7. **Kontrollü fark:** Tolerans dışı fark yeniden sayım veya yetkili onayı olmadan stok hareketine dönüşmez.
8. **Bağımsız yeniden sayım:** Politika isterse ikinci sayımı ilk sayımı yapan kullanıcı yapamaz ve önceki sonuçları göremez.
9. **Hareket bütünlüğü:** Onaylanan artı ve eksi farklar idempotent stok hareketleriyle işlenir. Bakiye tablolarına doğrudan SQL güncellemesi yapılmaz.
10. **UTC:** Operasyon zamanları UTC tutulur; kullanıcının zaman diliminde gösterilir.

## Sayım türleri

- `FullPhysical`: Depo veya alanın dönemsel tam sayımı
- `Cycle`: ABC/risk planına göre periyodik lokasyon veya stok sayımı
- `Spot`: Şüpheli stok için anlık nokta sayımı
- `ZeroCheck`: Boş olması beklenen rafın sıfır doğrulaması
- `Partial`: Belirli stok/lot/seri veya lokasyon alt kümesi

## Sayım modları

- `Blind`: Beklenen miktar gizlidir.
- `Open`: Beklenen miktar gösterilir.
- `DoubleBlind`: Birbirinden bağımsız iki kör sayım gerekir.

## Hareket politikası

- `Snapshot`: Operasyon devam eder; fark, serbest bırakma anındaki defter bakiyesine göre hesaplanır.
- `SnapshotWithMovementReconciliation`: Snapshot sonrası hareketler zaman damgası ve hareket kimliğiyle sayım anına uyarlanır.
- `LocationFreeze`: Sayılan lokasyonda fiziksel stok hareketleri geçici olarak engellenir.

Varsayılan politika `SnapshotWithMovementReconciliation` olmalıdır. Tam depo kilidi yalnız planlı dönem sonu sayımlarında kullanılmalıdır.

## Durum makinesi

```text
Draft -> Planned -> Released -> InProgress
                            -> AwaitingReview -> RecountRequired -> InProgress
                            -> AwaitingApproval -> Posting -> Completed
Draft/Planned/Released -> Cancelled
```

Geçişler yalnız application service tarafından yapılır. İstemci doğrudan `Status` gönderemez.

## Tablolar

### RII_INVENTORY_COUNT_HEADER

Belge, sayım türü/modu, hareket politikası, plan zamanı, öncelik, tolerans, snapshot hareket kimliği, durum ve özet sayaçları.

### RII_INVENTORY_COUNT_SCOPE

Depo, lokasyon, stok, yapılandırma kodu, stok grubu ve alt lokasyonları dahil etme kurallarını tutar.

### RII_INVENTORY_COUNT_TASK

Lokasyon bazlı fiziksel işi, rota sırasını, atanan kullanıcı/ekibi, tur numarasını ve görev durumunu tutar.

### RII_INVENTORY_COUNT_LINE

Snapshot boyutlarını ve miktarını, sayılan miktarı, farkı, tolerans sonucunu ve karar durumunu tutar. Beklenmeyen fiziksel stoklar da ayrı satır olarak kaydedilir.

### RII_INVENTORY_COUNT_ENTRY

Başarılı sayım girişlerini append-only olarak tutar. Barkod, cihaz, kullanıcı, miktar, tur ve idempotency anahtarı kaydedilir.

### RII_INVENTORY_COUNT_SCAN_EVENT

Yanlış raf, yanlış stok, geçersiz seri, fazla okutma ve mükerrer istek dahil bütün okutma girişimlerini denetim kanıtı olarak tutar.

### RII_INVENTORY_COUNT_REVIEW

Onay, ret, yeniden sayım ve tolerans override kararlarını gerekçe ve kullanıcıyla kaydeder.

### RII_INVENTORY_COUNT_ADJUSTMENT

Sayım satırı ile oluşturulan stok hareketi operasyonunu bağlar. Farkın iki kez post edilmesini engeller.

### RII_INVENTORY_COUNT_POLICY

Şube/depo bazlı varsayılan sayım modu, tolerans, deneme sayısı, yeniden sayım ve hareket politikalarını tutar.

## Operasyon akışı

1. Yönetici sayım tipini, kapsamını ve politikayı seçerek taslak oluşturur.
2. Önizleme; kaç raf, stok, seri ve satır oluşacağını gösterir.
3. Serbest bırakma transaction'ında snapshot, lokasyon görevleri ve sayım satırları üretilir.
4. Görevler kullanıcıya, ekibe veya sistem yönlendirmesine atanır.
5. Personel önce raf barkodunu okutur; sistem görev rafıyla birebir eşleştirir.
6. Stok/etiket barkodu çözülür. Seri/lot politikasına göre miktar istenir veya barkod miktarı kullanılır.
7. Beklenmeyen ürün politikaya göre engellenir ya da `Unexpected` satırı oluşturur.
8. Kullanıcı rafta başka ürün olmadığını açıkça doğrulayarak görevi tamamlar.
9. Sistem snapshot ve hareket watermark'ına göre farkları hesaplar.
10. Tolerans içi fark otomatik onaylanabilir; tolerans dışı fark incelemeye veya bağımsız yeniden sayıma gider.
11. Yetkili onayında artış ve azalışlar ayrı, idempotent stok hareketi operasyonları olarak post edilir.
12. Tüm satırlar post edildiğinde belge tamamlanır; iptalde post edilmiş hareketler ters kayıtla geri alınır.

## Yetkiler

- `WMS.INVENTORY_COUNT.VIEW`
- `WMS.INVENTORY_COUNT.CREATE`
- `WMS.INVENTORY_COUNT.UPDATE`
- `WMS.INVENTORY_COUNT.RELEASE`
- `WMS.INVENTORY_COUNT.ASSIGN`
- `WMS.INVENTORY_COUNT.COUNT`
- `WMS.INVENTORY_COUNT.REVIEW`
- `WMS.INVENTORY_COUNT.APPROVE`
- `WMS.INVENTORY_COUNT.POST`
- `WMS.INVENTORY_COUNT.CANCEL`
- `WMS.INVENTORY_COUNT.POLICY.VIEW`
- `WMS.INVENTORY_COUNT.POLICY.MANAGE`

## Ekranlar

1. **Sayım Merkezi:** Açık görevler, gecikenler, fark bekleyenler, bugünkü sayımlar ve doğruluk KPI'ları.
2. **Sayım Emirleri:** Ortak paged grid, gelişmiş filtre, kolon arama, export ve yetkili satır işlemleri.
3. **Yeni Sayım Sihirbazı:** Tip -> kapsam -> çalışma politikası -> atama -> önizleme -> serbest bırakma.
4. **Atanmış Sayımlarım:** El terminaline uygun, öncelik ve rota sıralı görev listesi.
5. **Sayım Çalışma Alanı:** Raf okut -> ürün/seri okut -> miktar -> başka ürün yok doğrulaması.
6. **Fark İnceleme:** Eski snapshot, sayım sonucu, snapshot sonrası hareketler, net fark, neden ve karar.
7. **Yeniden Sayım:** Önceki sonucu gizleyen bağımsız görev.
8. **Sayım Politikaları:** Açıklamalı parametreler ve örnek senaryolar.
9. **Sayım Raporları:** Doğruluk, fark, tekrar sayım, personel/raf performansı ve hareket bağlantıları.

## Fazlar

1. Domain, EF configuration, politika ve izin altyapısı
2. Taslak, kapsam, önizleme ve snapshot/release
3. Görev atama ve mobil barkodlu sayım
4. Fark inceleme, bağımsız yeniden sayım ve onay
5. Stok hareketine post, iptal ve ters kayıt
6. Cycle count planları, ABC/risk/threshold otomasyonu ve Hangfire
7. Raporlar, Excel/PDF ve performans testleri

