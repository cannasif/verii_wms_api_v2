# Warehouse Assistant 2.4 — Yerel Hibrit Semantik Motor

## Amaç

Warehouse Assistant 2.4, mevcut deterministik WMS dil ve güvenlik kurallarını yerel bir anlamsal benzerlik katmanıyla birleştirir. Amaç, yalnızca önceden yazılmış kelime kalıplarına bağlı kalmadan doğal kullanıcı sorularını doğru salt-okunur WMS sorgusuna yönlendirmektir.

Soru metni ve depo verisi harici bir servise gönderilmez. Yerel model yalnızca kullanıcının soru metninden bir vektör üretir; yetki, kullanıcı kapsamı, şube/depo erişimi, veri sorgusu ve cevap üretimi API'nin mevcut uygulama servislerinde kalır.

## Karar sırası

1. Yazma, silme, onaylama veya ERP'ye gönderme isteyen komutlar güvenlik çekirdeği tarafından reddedilir.
2. Barkod, seri, belge veya plaka gibi açık ve kesin sorgular doğrudan deterministik hızlı yoldan çözülür.
3. Diğer sorular hem kural motoru hem de yerel `embeddinggemma` modeliyle değerlendirilir.
4. Semantik benzerlik, kural kanıtı ve bulunan varlık kanıtı ağırlıklı olarak birleştirilir.
5. Sonuç eşik altında veya iki amaç birbirine çok yakınsa semantik tahmin uygulanmaz; güvenli kural sonucu kullanılır.
6. Seçilen amaçtan sonra mevcut yetki ve veri kapsamı kontrolleri çalışır. Yerel model doğrudan veritabanı sorgulayamaz.

## Yerel çalışma bileşenleri

- Sağlayıcı: Ollama loopback servisi
- Varsayılan adres: `http://127.0.0.1:11434`
- Model: `embeddinggemma`
- Ağ sınırı: yalnızca loopback HTTP/HTTPS adresine izin verilir
- Katalog: canlı veri içermeyen, kod incelemesine açık WMS amaç örnekleri
- Isıtma: model ve amaç kataloğu API başladıktan sonra arka planda hazırlanır
- Kesinti davranışı: zaman aşımı, geçersiz yanıt veya model yokluğunda deterministik motor çalışmaya devam eder
- Devre kesici: başarısız sağlayıcı belirlenen süre boyunca tekrar çağrılmaz

## Sunucu kurulumu

Ollama'yı API ile aynı sunucuya servis olarak kurun. Ardından bir kez:

```powershell
ollama pull embeddinggemma
```

Modelin erişilebilirliğini doğrulayın:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:11434/api/embed -ContentType 'application/json' -Body '{"model":"embeddinggemma","input":["task: classification | query: depo stok bakiyesi"]}'
```

Model dosyaları büyüktür ve Git deposuna eklenmez. Jenkins yalnızca uygulamayı yayınlar; Ollama ve model sunucu hazırlığının parçasıdır.

## Yapılandırma

```json
"WarehouseAssistant": {
  "Version": "2.4.0",
  "EnableOpenAiIntentResolution": false,
  "RoutingStrategy": "Hybrid",
  "BypassSemanticForExactLookups": true,
  "LocalEmbeddings": {
    "Enabled": true,
    "Endpoint": "http://127.0.0.1:11434",
    "Model": "embeddinggemma",
    "TimeoutMilliseconds": 5000,
    "FailureBackoffSeconds": 30,
    "MaximumBatchSize": 128,
    "MaximumInputCharacters": 600,
    "KeepAlive": "15m",
    "WarmOnStartup": true,
    "InputPrefix": "task: classification | query: ",
    "SemanticWeight": 0.65,
    "RuleWeight": 0.25,
    "EntityWeight": 0.10,
    "MinimumSemanticSimilarity": 0.42,
    "StrongSemanticSimilarity": 0.78,
    "MinimumHybridConfidence": 0.50,
    "AmbiguityMargin": 0.06
  }
}
```

Yerel model geçici olarak kapatılacaksa yalnızca `LocalEmbeddings:Enabled=false` yapılır. Bu durumda asistan 2.3'teki deterministik yerel motorla çalışmayı sürdürür.

## Operasyon ve gizlilik

- Ollama portunu internetten yayınlamayın; endpoint loopback olarak kalmalıdır.
- Soru metni uygulama loguna yazılmaz.
- Model girdisi karakter sınırıyla kısıtlanır ve kontrol karakterleri temizlenir.
- Model katalog seçimi yapar; SQL, EF sorgusu veya kullanıcı yetkisi üretemez.
- Asistan salt okunurdur. Operasyon oluşturan veya değiştiren komutlar model çağrısından önce engellenir.
- Sağlayıcı hatası API başlangıcını ve Warehouse Assistant'ın temel işlevini durdurmaz.

## Doğrulama ölçütleri

- Yerel hibrit yönlendirme ve güvenlik testleri
- Düşük güven ve belirsizlikte deterministik geri dönüş
- Kesin barkod/seri sorgularında model çağrısını atlama
- Model hatasında devre kesici ve güvenli geri dönüş
- Amaç kataloğunu süreç başına yalnızca bir kez hazırlama
- Konuşma geçmişinde kullanıcıya gösterilen yorumun korunması
- API ve web production build
- Gerçek `embeddinggemma` ile Türkçe doğal cümle kalibrasyonu

Bu sürüm veritabanı şemasını değiştirmez; migration gerekmez.
