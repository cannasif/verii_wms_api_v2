# KKD: WMS V1 kural analizi ve WMS V2 geçiş tasarımı

## 1. V1'deki iş kuralları

V1 KKD modülü organizasyon, hak, dağıtım ve raporlama olarak dört parçadan oluşur:

- Personel; Netsis cari kartı, departman, rol, işe giriş tarihi ve QR kodu ile tanımlanır.
- Hak matrisi departman + rol için tarihe bağlı olarak çalışır. Stok bazlı kural grup bazlı kuraldan daha özeldir ve önce seçilir.
- Bir kural başlangıç, belirli ay sonrası ve periyodik fazlar taşıyabilir. Periyodik dönem işe giriş yıl dönümüne sabitlenir.
- Kural; dönem miktarı, dağıtım sıklığı, sıklık başına miktar, toplu teslim izni, yıllık teslim sayısı, yıllık miktar ve devreden üst sınır içerebilir.
- Personel ek hakkı tarih aralığı ve opsiyonel kural bağlantısıyla ana matrise eklenir.
- Dağıtım, personelin Netsis'teki açık siparişiyle doğrulanır. Hak yetmezse yalnızca açık sipariş bakiyesi kadar `OpenOrderExcess` teslimine izin verilir.
- Tamamlanan dağıtım hak tüketimine dönüşür; iptal edilen teslimin tüketimi ters kayıtla geri alınır.

V1'de ayrıca eski `KkdEntitlementPolicy` modeli vardır. Aktif hak hesaplayıcısı bu modeli kullanmadığı için aynı iş için ikinci ve çelişen bir motor oluşturur. V2'de bu kayıtlar matrise dönüştürülecek, çalışma zamanında ayrı motor olarak yaşatılmayacaktır.

## 2. V1'de düzeltilen mimari açıklar

- V1 dağıtımı yerel bakiyeyi doğrudan azaltır; izlenebilir ve terslenebilir stok hareketi üretmez.
- V1 ilk siparişi yalnızca yerel KKD sipariş tablosuna yazar; dağıtımın aradığı Netsis açık siparişini üretmez.
- Yıllık sayı/miktar, devreden miktar ve toplu teslim alanlarının bir bölümü entity'de bulunmasına rağmen hak motorunda eksik uygulanır.
- Aynı personelin birden fazla ek hakkı tek bir kaynak gibi ele alınabilir.
- Dağıtım ile ERP çıkışı atomik/idempotent bir süreç olarak bağlı değildir.

## 3. V2 hedef akışı

1. Departman, rol ve personel şube kapsamında tanımlanır.
2. Tarih etkin matriste stok kuralı, yoksa stok grup kuralı seçilir.
3. Başlangıç/ay sonrası/periyodik faz, yıllık ve sıklık sınırları birlikte hesaplanır.
4. Dağıtım taslağında ana hak ve tüm ek hak kaynakları ayrı ayrı rezerve edilir.
5. Personelin Netsis carisi, açık sipariş satırı, stok, depo, seri/lot ve lokasyon kuralları doğrulanır.
6. KKD dağıtımı V2 `WarehouseOutbound` taslağı oluşturur; stok doğrudan değiştirilmez.
7. Fiziksel toplama/yükleme/sevk, standart ambar çıkış operasyonundan yürür.
8. Sevk kesinleştiğinde immutable stok hareketi yazılır, KKD rezervasyonu tüketime dönüşür ve Netsis ambar çıkışı otomatik gönderilir.
9. ERP gönderimi başarısız olursa sevk/hak ikinci kez yazılmaz; aynı işlem yeniden ERP'ye gönderilebilir.
10. İptalde önce ERP/WMS ambar çıkışı terslenir, ancak başarılı ters kayıttan sonra KKD tüketimi terslenir.

## 4. Geçiş aşamaları

- Aşama 1: Organizasyon, matris/faz, ek hak, hak sorgusu, dağıtım ve ambar çıkış bağlantısı.
- Aşama 2: V1 verisini tekrar çalıştırılabilir ETL ile yeni matrise taşıma ve mutabakat raporu.
- Aşama 3: İlk KKD siparişini gerçek Netsis sipariş servisine bağlama.
- Aşama 4: Kalan hak, tüketim, validasyon ve departman/rol/grup raporları.

## 5. Değişmezler

- Şube istemciden serbest metin olarak güvenilir kaynak kabul edilmez; oturum kapsamı kullanılır.
- Hak rezervasyonu ve tüketimi silinmez/güncellenmez; ters kayıt üretilir.
- ERP çıktısı oluşmadan yerel hak serbest bırakılmaz.
- Aynı idempotency anahtarı stok, hak veya ERP belgesini ikinci kez oluşturamaz.
- Seri/lot ve kaynak raf kuralları standart ambar çıkış motoruyla aynıdır.

## 6. Şube bazlı süreç politikası

KKD operasyon parametreleri oturum şubesi kapsamında tek bir `DEFAULT` politika kaydıyla yönetilir. Kayıt henüz oluşturulmamışsa güvenli varsayılanlar çalışma zamanında uygulanır ve salt okuma işlemi veritabanına satır yazmaz.

- `RequireOpenOrder`: Varsayılan olarak açıktır. Her dağıtım kalemi canlı Netsis açık sipariş satırına bağlı olmak zorundadır.
- `AllowOpenOrderExcess`: Açık sipariş bakiyesi varsa hesaplanan KKD hakkının üstünde teslim yapılıp yapılamayacağını belirler.
- `AllowMultipleOrdersPerDistribution`: Tek dağıtım belgesinde birden fazla Netsis siparişinin kullanılmasını belirler.
- `RequireEmployeeUserLink`: Teslim alan personelin aktif bir WMS kullanıcı hesabına bağlı olmasını zorunlu kılar.
- `AllowFutureDatedDistribution`: İleri tarihli KKD çıkış belgesi oluşturulmasına izin verir.

Politika gevşetilse bile oturum şubesi, personel–cari bağlantısı, canlı Netsis sipariş bakiyesi, sipariş satırı–stok eşleşmesi, kaynak depo/raf, stok takip kuralları, idempotency ve gerçek ambar çıkışı kontrolleri kapatılamaz. Politika API'de uygulanır; yalnızca arayüz görünürlüğüne güvenilmez.
