# Warehouse Assistant 2.5 Baseline ve 2.8 Geliştirme Planı

Bu belge, üretim kodu değiştirilmeden önce 17 Ağustos 2026 tarihinde çıkarılan
başlangıç durumunu ve mevcut mimariyi bozmadan uygulanacak 2.6–2.8 planını kaydeder.
Kapsam yalnızca `verii_wms_api_v2` ve `verii_wms_web_v2` repolarıdır. Eski WMS
repoları incelenmemiştir.

## Git ve çalışma alanı başlangıcı

- Web: `main`, `origin/main` ile eşit, çalışma ağacı temiz.
- API: `main`, `origin/main` ile eşit.
- API'de kullanıcıya ait `appsettings.json` bağlantı dizesi değişikliği vardır. Bu
  dosya Assistant çalışmasına dahil edilmeyecek, geri alınmayacak ve commitlenmeyecektir.
- Migration, seed, dış servis, gerçek müşteri verisi veya production bağlantısı
  kullanılmayacaktır.

## Uçtan uca mimari harita

### Web

1. Route: `/warehouse/assistant`; lazy loader `src/app/route-loaders.ts`, route kaydı
   `src/app/App.tsx`.
2. Menü: `src/components/shared/nav-items.tsx`; arama alias'ları `asistan`, `chatbot`,
   `yapay zeka`, `stok sor`, `seri sor`, `işlemlerim`.
3. Girdi: `WarehouseAssistantPage.tsx` içindeki 1.000 karakter sınırlı `textarea`.
   Enter gönderir, Shift+Enter yeni satır açar.
4. İstemci: `warehouse-assistant.api.ts` aşağıdaki uçları çağırır:
   - `GET /api/warehouse-assistant/capabilities`
   - `GET /api/warehouse-assistant/conversations`
   - `GET /api/warehouse-assistant/conversations/{id}/messages`
   - `POST /api/warehouse-assistant/chat`
   - `POST /api/warehouse-assistant/conversations/{id}/archive`
5. Parametre yardımı: web, doğrulanmış `settings-guidance` kataloğundan bir ipucu
   bulursa yapılandırılmış `parameterHint` gönderir. Domain sorgu kuralları web'e
   taşınmaz.
6. Sonuç: cevap metni, yorumlanan intent/güven, entity seçimleri, metrikler,
   istisnalar, izlenebilirlik, aktivite, seri, stok/lokasyon, barkod, hareket, görev,
   mal kabul, araç ve transfer kartları ile kanıt bölümü gösterilir.
7. Geçmiş: yalnızca kullanıcının konuşmaları listelenir; yapılandırılmış sonuç JSON'u
   geçmiş açıldığında tekrar kartlara dönüştürülür.
8. Dışa aktarma: sonuç kümeleri Excel/PDF modeline çevrilir. Bu katman sorgu yapmaz.

### API

1. Controller `[Authorize]`, `warehouse-assistant` rate-limit politikası, JWT kullanıcı
   kimliği ve zorunlu `BranchCode` claim'i ile başlar.
2. `ResolveAccessAsync` her istekte standart modül permission'larını ayrı ayrı çözer.
3. `LocalHybridWarehouseAssistantIntentResolver` mesajı sınırlar, önce salt-okunur
   politikasını çalıştırır, konuşma düzeltmelerini uygular ve en çok üç sorguluk planı
   oluşturur.
4. `WarehouseAssistantIntentResolver` normalizasyon, tarih/seri/barkod/plaka/belge
   çıkarımı ve deterministik intent önceliğini uygular.
5. `LocalWarehouseLanguageEngine` ikinci bir puanlama kanıtı üretir. Tek sinyal yerine
   intent sinyallerini toplar; düşük skor veya yakın iki adayda `Unknown` döner.
6. `WarehouseAssistantService` konuşma sahipliğini doğrular, son doğrulanmış context'i
   okur, planı genişletir ve yalnızca sabit handler switch'indeki salt-okunur sorguları
   çalıştırır. Doğal dilden SQL üretilmez.
7. Entity çözümü stok ve cari kayıtlarını branch içinde en çok 5.000 adaydan puanlar.
   Tek kesin kod/ad eşleşmesi yoksa en çok sekiz aday döndürür; rastgele seçim yapmaz.
8. Handler'lar EF sorgularını tip güvenli kurar, sonuçları çoğunlukla 50 satırla sınırlar
   ve kullanıcı depo erişimini `UserWarehouseAccessService` ile uygular.
9. Kullanıcı ve assistant mesajı, yapılandırılmış sonuç ve kısa doğrulanmış context aynı
   correlation id ile kaydedilir. Audit kaydı soru metnini değil intent, araç, kapsam ve
   sonuç sayısını taşır.
10. Conversation okuma/arşivleme `conversation.UserId + BranchCode` ile korunur.

## Mevcut intent kataloğu ve sorgu kanıtı

| Intent | Örnek | Güvenli sorgu/veri | Cevap |
|---|---|---|---|
| `Help` | “Ne sorabilirim?” | Capability kataloğu | Yetkiye göre örnekler |
| `MyActivities` | “Bugün yaptığım işlemler” | `AuditLog`, branch+tarih+self, Assistant logları hariç | Aktivite listesi |
| `UserActivities` | “Herkes dün ne yapmış?” | `AuditLog` + kullanıcı; `QUERY_ALL_USERS` | Seçili/tüm kullanıcı aktivitesi |
| `SerialBalance` | “DTG-1 seri bakiyesi nerede?” | `LocationStockBalance` + depo+lokasyon+stok; seri, non-zero, yetkili depo | Fiziksel/rezerve/kullanılabilir seri bakiyesi |
| `SerialReceiptHistory` | “DTG-1 kim tarafından alındı?” | Posted, terslenmemiş mal kabul stok hareketi | Mal kabul/aktör geçmişi |
| `StockLocationBalance` | “01/013 hangi raflarda?” | Doğrulanmış stok + `LocationStockBalance`, yetkili depo | Lokasyon bazlı üç bakiye |
| `BarcodeLookup` | “GRL-000123 hangi stok?” | Merkezi barcode resolver, ardından yetkili depo bakiyesi | Barkod alanları ve lokasyonlar |
| `StockMovementHistory` | “01/013 stok hareketleri” | Hareket entry+operation+stok+depo+lokasyon, tarih, yetkili depo | Giriş/çıkış ledger'ı |
| `AssignedTasks` | “Bana atanan açık emirler” | Mal kabul, transfer, sevk, inbound/outbound assignment sorguları | Öncelikli açık görevler |
| `GoodsReceiptAnalysis` | “ASD'den dün ne geldi?” | Header+line+depo, belge tarihi, cari/stok, non-cancelled | Belge/satır/miktar özeti |
| `ParameterHelp` | “Fazla kabul ayarı ne yapar?” | Doğrulanmış parameter hint; veri sorgusu yok | Web guidance kataloğu |
| `SteelVehicleAnalysis` | “34 ABC 123 bugün geldi mi?” | Branch+plaka+tarih araç girişleri ve kabul edilen levhalar | Araç/kabul özeti |
| `WarehouseTransferAnalysis` | “Bu hafta üretim transferleri” | Header+line, tarih, scope, belge ve yetkili kaynak/hedef depo | Transfer miktar/status özeti |
| `ShiftBrief` | “Mesaiye başladım” | Yetkili görevler + operasyonel istisnalar | Metrikler ve öncelikler |
| `OperationalExceptions` | “Acil bakmam gerekenler” | Önceden tanımlı bakiye, GR, transfer, sevk, kalite, packing kuralları | Şiddet sıralı istisnalar |
| `Traceability` | “DTG-1 başına ne geldi?” | Seri hareket ledger'ı, kronolojik, yetkili depo | Uçtan uca timeline |
| `ProcessBlockers` | “GRI-... neden bekliyor?” | İzin verilen GR/transfer/sevk status ve gate kontrolleri | Onay/kalite/putaway/ERP engelleri |
| `Composite` | “Stok nerede; hareketleri de” | Her alt sorgu ayrı yetki kontrolüyle sıralı çalışır | Birleştirilmiş cevap |

## Güvenlik ve veri sınırları

- Authentication: controller seviyesinde zorunlu.
- Tenant/company karşılığı: tüm conversation ve operasyon sorgularında zorunlu
  `BranchCode`. Helper branch kapsamını genişletemez.
- Depo izolasyonu: bakiye, hareket, görev, mal kabul, transfer, sevk, kalite ve ilgili
  sorgularda kullanıcı depo atamaları uygulanır.
- Permission: intent handler veri çözümlemeden önce ilgili standart modül view
  permission'ını kontrol eder.
- IDOR: conversation okuma/arşivleme kullanıcı+branch+aktif durum ile yapılır.
- Injection: serbest SQL/URL/tool adı kabul edilmez; yalnızca sabit intent handler'ları
  EF expression üretir.
- Kaynak limitleri: mesaj 1.000 karakter, plan en çok 3 alt sorgu, genel sonuç 50,
  konuşma geçmişi 200 mesaj, explicit tarih aralığı en çok 366 gün.
- Log gizliliği: audit metadata soru metnini içermez. Mesaj yalnızca sahipli konuşma
  kaydında saklanır.
- Bilinen kanıt eksiği: mevcut `Evidence.Filters` yalnızca genel “yetkili kapsam” metni
  gösterir; gerçek depo/tarih/entity/ölçü filtreleri 2.7'de görünür yapılacaktır.

## Kodda bulunan fakat 2.5 Helper'ın kullanmadığı güvenli veri alanları

- Depo kartları ve aktif/pasif, kapasite bilgili lokasyonlar.
- Depo/stok toplu bakiye projeksiyonu.
- Sayım header/task/line ve variance verileri; kitap miktarı/fark için ayrıca
  `INVENTORY_COUNT.REVIEW` gerekir.
- Generator Production proje, operasyon, malzeme coverage, kalite gate ve gecikme
  alanları; standart ekran permission'ı `WMS.GENERATOR_PRODUCTION.VIEW`.
- Web'de doğrulanmış route ve iş akışları: ERP stok kartı, mal kabul, transfer, sayım,
  stok hareketleri ve Generator Production projeleri.

Kodda doğrulanamayan sınırlar:

- Warehouse entity'sinde aktif/pasif alanı yoktur. “Aktif depo” tahmin edilmeyecektir.
- Stock entity'sinde kritik/minimum stok eşiği yoktur. “Kritik stok” için eşik
  uydurulmayacaktır.
- Tüm sipariş türlerini birleştiren tek bir sipariş projection'ı yoktur; mevcut görev
  listesi tam sipariş durumu gibi sunulmayacaktır.
- Location kapasitesinde farklı birimler tek toplam/doluluk oranında birleştirilmeyecektir.

## 2.5 başlangıç değerlendirmesi

Makine-okunur corpus:
`tests/QueryTests/WarehouseAssistantBaselineEvaluationTests.cs`.

Her satır şu bilgileri taşır: soru, hedef intent, beklenen parametreler, filtreler,
veri kaynağı, cevap türü, güvenlik beklentisi ve gerekirse doğrulanmış conversation
context'i.

- Corpus: 75 soru.
- Hedef intent doğru: **31/75 (%41,3)**.
- Mevcut/önceden desteklenen ve güvenlik ağırlıklı 40 soruda doğru: **31/40 (%77,5)**.
- İyileştirme corpusundaki 35 yeni depo/lokasyon/içgörü/sayım/üretim/navigasyon
  sorusunda doğru: **0/35**.
- Başlangıç ilgili API testleri: 126/126 başarılı.
- Başlangıç web Assistant testleri: 10/10 başarılı.

Başlangıçta hedefle aynı intent'e giden kayıtlar:

`CUR-001, CUR-002, CUR-003, CUR-005, CUR-007, CUR-008, CUR-009, CUR-013,
CUR-015, CUR-016, CUR-018, CUR-019, CUR-020, CUR-021, CUR-024, CUR-025,
CUR-026, CUR-027, CUR-028, CUR-029, CUR-030, CUR-031, CUR-032, CUR-033,
CUR-034, CUR-035, CUR-036, SEC-001, SEC-002, SEC-003, SEC-004`.

Başlangıç başarısızlıkları:

| Kayıt | Hedef | 2.5 sonucu | Neden / mevcut cevap davranışı |
|---|---|---|---|
| CUR-004 | UserActivities | MyActivities | Adı intent seviyesinde hedef kullanıcı sinyali saymıyor; servis yetkiye göre sonradan arıyor |
| CUR-006 | SerialBalance | Unknown | “nerde” varyasyonu ve düşük sinyal birleşimi yetersiz; genel fallback |
| CUR-010–012 | StockLocationBalance | Unknown | Günlük/kırık kelime sırası ve yazım hatalı ürün/depo sinyalleri yetersiz; genel fallback |
| CUR-014 | StockLocationBalance | Unknown | Context üstünde bakiye ölçüsü devamı çözülemiyor; genel fallback |
| CUR-017 | StockMovementHistory | Unknown | “dün çıkışları” hareket alias'ı değil; genel fallback |
| CUR-022–023 | GoodsReceiptAnalysis | Unknown | “bugün gelen ürünler” ve kalite bekleyen kabul filtreleri tanımlı değil; genel fallback |
| WHS-001–005 | WarehouseOverview | Unknown | Depo overview intent/handler yok; genel fallback |
| LOC-001 | LocationInventory | StockLocationBalance | Lokasyon kodunu ürün sorgusu gibi yorumluyor; yanlış entity handler'ı |
| LOC-002–005 | LocationInventory | Unknown | Lokasyon boşluk/kapasite/tür sorguları yok; genel fallback |
| INS-001–005 | InventoryInsights | Unknown | Zero/ranking/group/measure query planı yok; genel fallback |
| INS-006 | InventoryInsights | OperationalExceptions | “kritik” tek başına exception'a yönlendiriyor; stok eşiği olmadığı açıklanmıyor |
| CNT-001–005 | InventoryCountAnalysis | Unknown | Sayım intent/handler/permission kapsamı yok; genel fallback |
| PRD-001–002,004–006,008 | GeneratorProductionAnalysis | Unknown | Generator Production intent/handler yok; genel fallback |
| PRD-003 | GeneratorProductionAnalysis | WarehouseTransferAnalysis | Üretim+malzeme ifadesi production transfer ile çakışıyor |
| PRD-007 | GeneratorProductionAnalysis | OperationalExceptions | “geciken” genel exception sinyaline gidiyor |
| NAV-001–002,004,006 | NavigationHelp | Unknown | Doğrulanmış route/workflow kataloğu yok; genel fallback |
| NAV-003 | NavigationHelp | WarehouseTransferAnalysis | “transfer” navigation niyeti veri analizi sanılıyor |
| NAV-005 | NavigationHelp | StockMovementHistory | “stok hareketleri ekranı” veri sorgusu sanılıyor ve sonra entity ister |

`Unknown` servis cevabı genel desteklenmeyen-soru metni ve yetkiye göre örnek öneriler
döndürür. Yanlış intent'lerde ilgili handler eksik entity isteyebilir veya yanlış rapora
hazırlanabilir; veri handler'ı kendi permission kontrolü olmadan çalışmaz.

## Uygulanacak 2.6–2.8 tasarımı

### 2.6 — Dil çekirdeği ve ölçülebilir karar

- Normalizasyonu ayrı, test edilebilir bir bileşene çıkar.
- Türkçe karakter, noktalama, ek, günlük yazım hatası, birim, sayı ve tarih davranışını
  orijinal metni kaybetmeden standartlaştır.
- Domain alias'larını merkezi, isimli ve salt-okunur bir sözlükte topla.
- Navigation, depo, lokasyon, içgörü, sayım ve Generator Production sinyallerini
  deterministik öncelikle ekle.
- Negation/exclusion ve “nasıl/nerede ekran” öğretici niyetini yazma komutundan ayır.
- Güveni skor, margin, entity ve zorunlu parametre kanıtından türet; neden kodlarını
  test edilebilir yap.

### 2.7 — Tip güvenli query planı, entity ve context

- Intent'ten sonra yalnızca enum ve doğrulanmış alanlar taşıyan query planı üret:
  query kind, entity adayları, warehouse/location, tarih, ölçü, status, sıralama,
  limit, exclusion, gerekli permission.
- Warehouse/location/project çözümünü branch ve yetkili depo aday havuzu içinde yap.
- Aynı kod/ad birden fazla kayda giderse seçim iste; yetkisiz adayları hiç gösterme.
- Context'e doğrulanmış warehouse/location/query-kind/measure ekle; kullanıcı+branch
  sahipli mevcut conversation kaydında kalmasını sürdür.
- Kanıt ve yorum kartlarında uygulanan gerçek filtreleri, required permission'ı ve
  intent nedenlerini göster.

### 2.8 — Yeni salt-okunur WMS handler'ları

- `WarehouseOverview`: erişilebilir depo sayısı/listesi, lokasyonlar ve birim bazlı
  depo toplamları. Aktif depo alanı olmadığı açıkça belirtilir.
- `LocationInventory`: lokasyon içeriği, boş/dolu ayrımı, tip ve güvenli kapasite özeti.
- `InventoryInsights`: zero/non-zero, en yüksek/en düşük, grup ve bakiye ölçüsü. Kritik
  eşik yoksa veri uydurmadan domain sınırı cevabı.
- `InventoryCountAnalysis`: açık sayımlar; REVIEW permission varsa variance/ranking.
- `GeneratorProductionAnalysis`: proje/operasyon durumu, malzeme açığı, kalite gate,
  gecikme ve planlanan/gerçekleşen karşılaştırması.
- `NavigationHelp`: yalnızca doğrulanmış route ve mevcut iş akışları; permission'a göre
  gösterim. Stok kartının WMS'te oluşturulmadığı, ERP'den senkronlandığı doğru anlatılır.
- Mevcut stok, hareket ve mal kabul handler'larına warehouse/location/measure/status/
  exclusion/explicit-date filtreleri eklenir.

## Clarification ve fallback kuralları

1. Zorunlu entity yoksa yalnız eksik alan sorulur.
2. Birden fazla yetkili entity adayı varsa en çok sekiz seçim döner.
3. Orta güven veya yakın intent skorunda algılanan yorum gösterilip onay istenir.
4. Düşük güvende veri sorgusu çalışmaz; desteklenen örnekler gösterilir.
5. Yetkisiz entity'nin varlığı, aday sayısı veya kodu açıklanmaz.
6. Domain alanı yoksa (kritik eşik, aktif depo) “0” döndürülmez; alanın mevcut olmadığı
   açıkça belirtilir.
7. Bulunan entity + boş veri ile entity bulunamaması ayrılır.

## Mikro commit planı

1. `test(assistant): establish 2.5 evaluation baseline`
2. `refactor(assistant): centralize local text normalization and terminology`
3. `feat(assistant): add deterministic typed query planning`
4. `feat(assistant): resolve warehouse and location scope safely`
5. `feat(assistant): add warehouse location and inventory insights`
6. `feat(assistant): add inventory count analysis with review gating`
7. `feat(assistant): expose generator production analysis`
8. `feat(assistant): add verified navigation guidance`
9. `feat(assistant-ui): render query filters and new result cards`
10. `test(assistant): cover authorization context ambiguity and regressions`
11. `docs(assistant): publish 2.8 capability and comparison guide`

Her commit explicit dosya/hunk staging, staged diff kontrolü ve ilgili testten sonra
oluşturulacaktır. Push, rebase, squash, PR veya migration yapılmayacaktır.

