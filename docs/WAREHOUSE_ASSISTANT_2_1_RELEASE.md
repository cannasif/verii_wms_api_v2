# Depo Asistanı 2.1

## Amaç

2.1 sürümü, yalnızca sabit kelime kalıplarıyla çalışan yönlendirmeyi hibrit bir anlama katmanına dönüştürür. Operasyon sorguları yine yalnızca izinli WMS servisleri üzerinden okunur; dil modeli SQL üretmez, yetki kararı vermez ve kayıt değiştiremez.

## Sorgu akışı

1. Mesaj uzunluğu, kullanıcı, şube, depo ve modül kapsamı doğrulanır.
2. Barkod, seri, plaka veya belge numarası açıkça verilmiş güvenli sorgular gecikmesiz yerel çözümleyicide çalışır.
3. Serbest cümleler anlam tabanlı yönlendiriciye gönderilir.
4. Yönlendirici yalnızca tanımlı WMS sorgu niyetlerinden birini ve yapılandırılmış alanları döndürebilir.
5. Güven skoru eşik altındaysa yanlış rapor çalıştırılmaz; kullanıcının dilinde tek bir netleştirme sorusu sorulur.
6. Sağlayıcıya erişilemezse servis kapanmaz; genişletilmiş yerel çözümleyiciye güvenli biçimde döner.
7. Son doğrulanmış konu ve varlık bağlamı konuşmada saklanır. Örneğin, bir mal kabul raporundan sonra “Peki bu hafta?” aynı rapor türünü yeni tarih aralığıyla çalıştırır.

## Çalıştırma ayarları

Uygulama deposunda anahtar tutulmaz. Ortam değişkenleri veya secret store kullanılmalıdır:

```text
WarehouseAssistant__EnableOpenAiIntentResolution=true
WarehouseAssistant__Model=gpt-5.6-luna
OPENAI_API_KEY=<secret-store-değeri>
```

`OPENAI_API_KEY` yerine `WarehouseAssistant__ApiKey` de kullanılabilir. Anahtar yoksa capabilities uç noktası `semanticRoutingAvailable=false` döndürür ve web arayüzü “Temel dil anlama” durumunu gösterir.

## Güvenlik sınırları

- Dil modeli yalnızca sorgu sınıflandırır ve varlık ipuçlarını çıkarır.
- Son kullanıcı yetkileri model cevabından bağımsız olarak API servisinde uygulanır.
- Sadece önceden tanımlanmış read-only sorgular çalıştırılır.
- Modelden gelen serbest SQL, URL veya araç adı kabul edilmez.
- Belirsiz kullanıcı, stok, cari ve belge ifadeleri otomatik seçilmez; aday veya netleştirme döndürülür.
- API anahtarı, mesaj içeriği veya sağlayıcı cevabı uygulama ayarlarına ve Git geçmişine yazılmaz.

## Yayın doğrulaması

- API capabilities cevabında sürüm, yönlendirme modu, model ve anlamsal kullanılabilirlik kontrol edilir.
- Kesin barkod/seri sorgularının sağlayıcı çağrısı yapmadan çalıştığı doğrulanır.
- Doğal Türkçe, İngilizce ve mevcut desteklenen dil örnekleri regresyon testinden geçirilir.
- Düşük güvenli model cevabının sorgu çalıştırmak yerine netleştirme istediği doğrulanır.
- Sağlayıcı hatasında API'nin yerel çözümleyiciyle cevap vermeye devam ettiği doğrulanır.

## Sonraki sürüm adayı

2.2 için önerilen kapsam, tek mesajdaki birden fazla bağımsız soruyu güvenli bir sorgu planına ayırmak ve her alt sorgunun yetkisini ayrı değerlendirmektir. Bu özellik 2.1'e bilinçli olarak dahil edilmemiştir; tek niyetli sorgularda doğruluk ve denetlenebilirlik korunmuştur.
