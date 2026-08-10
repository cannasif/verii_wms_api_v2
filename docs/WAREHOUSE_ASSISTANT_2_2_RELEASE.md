# Warehouse Assistant 2.2

## Amaç

2.2 sürümü, yardımcının yalnızca tek cümle/tek niyet eşleştiren bir yapıdan kontrollü bir depo sorgu planlayıcısına geçişidir. Yardımcı hâlâ yalnızca izinli ve salt okunur application servislerini çalıştırır; modelin SQL üretmesine, yetki kararı vermesine veya veri değiştiren işlem seçmesine izin verilmez.

## Sorgu planlama

- Tek mesaj en fazla üç bağımsız WMS sorgusuna ayrılabilir.
- İlk sorgu ana plan, diğer ikisi ek plan öğeleridir.
- Noktalı virgül, yeni satır ve açık bağlaçlarla ayrılmış, yerel çözümleyicinin kesin anlayabildiği sorular AI bağlantısı olmadan da çalışır.
- Daha doğal ve birleşik ifadeler semantic resolver tarafından yapılandırılmış strict function schema ile ayrılır.
- Bir alt sorgu bile belirsiz, düşük güvenli, desteklenmeyen veya yazma amaçlı ise planın hiçbiri çalıştırılmaz; kullanıcıdan tek bir netleştirici bilgi istenir.
- Alt sorgular aynı DbContext üzerinde sıralı çalıştırılır. Her alt sorgu kendi kullanıcı, şube, depo ve modül yetki kontrolünden geçer.
- Sonuçlar tek cevapta birleştirilir; her veri koleksiyonu genel sonuç sınırına tabi tutulur.

## Konuşma hafızası

Context JSON aşağıdaki kısa süreli, doğrulanmış bilgileri taşır:

- son başarılı intent ve tarih filtresi,
- son başarılı kullanıcı sorusu,
- netleştirme bekleyen soru,
- doğrulanmış stok, seri, barkod, cari, plaka ve belge bilgileri,
- hedef kullanıcı ve tüm kullanıcılar kapsamı.

Belirsiz bir cevap önceki doğrulanmış bağlamı silmez. Kullanıcının sonraki kısa cevabı (`STK-1`, `geçen hafta`, `Ahmet`) bekleyen soruyla birlikte yeniden çözülür. Başarılı çözümden sonra bekleyen soru temizlenir.

## Model çağrısı

- Responses API kullanılır.
- `store=false` ile sağlayıcı tarafı yanıt saklama kapalıdır.
- Yalnızca zorunlu, strict function call kabul edilir.
- `parallel_tool_calls=false` tutulur.
- Niyet planlama için düşük reasoning effort kullanılır.
- Sağlayıcı süre, plan öğesi sayısı ve token kullanımı içeriksiz telemetry olarak loglanır; kullanıcı sorusu loglanmaz.
- Hata, timeout veya geçersiz çıktı halinde güvenli deterministic resolver devreye girer.

## Sürüm ve yapılandırma

```text
WarehouseAssistant__Version=2.2.0
WarehouseAssistant__EnableOpenAiIntentResolution=true
WarehouseAssistant__Model=gpt-5.6-luna
WarehouseAssistant__RoutingStrategy=Hybrid
WarehouseAssistant__MinimumSemanticConfidence=0.72
OPENAI_API_KEY=<secret store>
```

API anahtarı appsettings veya kaynak koduna yazılmaz. Ortam secret store üzerinden `OPENAI_API_KEY` adıyla verilir.

## Doğrulama kapsamı

- strict read-only schema,
- düşük güvenli sonuçta güvenli durma,
- bileşik semantic plan,
- bileşik deterministic plan,
- alt sorgu belirsizliğinde planın tamamını durdurma,
- kısa netleştirme cevabını bekleyen soruyla birleştirme,
- iki farklı yetkili sorguyu tek cevapta yürütme,
- konuşma context'inde pending/success durum geçişi,
- yedi web dilinde aynı localization anahtarları,
- API Release build ve web production build.

## Operasyonel sınırlar

- En fazla üç alt sorgu çalışır.
- Her sonuç türü en fazla 50 satır döndürür.
- Yazma, onay, iptal, ERP post, serbest SQL ve yetki yükseltme isteği sorgu planına alınmaz.
- AI bağlantısı yokken daha dar ama güvenli yerel çözümleme devam eder; arayüz aktif modu açıkça gösterir.
