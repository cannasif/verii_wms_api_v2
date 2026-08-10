# Warehouse Assistant 2.3 — Yerel Dil Motoru

## Amaç

Warehouse Assistant 2.3, internet veya harici yapay zekâ servisi olmadan çalışan yerel WMS soru çözümleme sürümüdür. Soru metni uygulamanın dışına gönderilmez. Yetki, şube, depo ve modül kapsamı kontrolleri önceki sürümlerde olduğu gibi API servis katmanında uygulanmaya devam eder.

## İşleyiş

1. Metin Türkçe karakter, büyük/küçük harf ve boşluk açısından normalize edilir.
2. WMS kavramları Türkçe çekim ekleriyle eşleştirilir. Örnek: `stok`, `stokta`, `stoktaki`, `stokların`.
3. Dört ve daha uzun kelimelerde sınırlı yazım hatası toleransı uygulanır. Bu kontrol stok kodu, seri, barkod ve belge numarası gibi kimliklerde kullanılmaz.
4. Olası amaçlar puanlanır. En güçlü amaç yeterli puana ve ikinci adaydan güvenli bir farka sahip değilse sistem tahmin üretmez.
5. Tarih, stok, seri, barkod, plaka, cari ve belge bilgileri ayrı olarak çıkarılır.
6. Konuşmadaki son doğrulanmış amaç ve varlıklar kısa takip sorularında yeniden kullanılır.
7. Bir mesajdaki en fazla üç bağımsız salt-okunur soru ayrı plan maddelerine bölünür.
8. Son aşamada yetki ve veri kapsamı kontrol edilir; kullanıcı yalnızca erişebildiği WMS verisini görür.

## Desteklenen doğal söyleyiş örnekleri

- `01/013 depoda nerelere dağılmış?`
- `ABC tedarikçisinden geçen hafta neler gelmiş?`
- `34 ABC 123 bugün geldi mi?`
- `DTG-1 ilk girişten bugüne hangi adımlardan geçmiş?`
- `Üretime giden malzemelerden eksik kalan var mı?`
- `Bana atanan emrleri gster`
- `Bugün yaptığım işemleri göser`

## Güvenlik sınırı

Asistan salt okunurdur. `sil`, `ekle`, `güncelle`, `onayla`, `iptal et`, `ERP'ye gönder`, `kaydet` ve benzeri değişiklik isteyen komutlar bir sorgu niyeti gibi yorumlanmaz. Bu güvenlik kontrolü veri servisine ulaşmadan önce uygulanır.

## Varsayılan çalışma ayarı

```json
"WarehouseAssistant": {
  "Version": "2.3.0",
  "EnableOpenAiIntentResolution": false,
  "RoutingStrategy": "DeterministicOnly"
}
```

Capabilities yanıtında çalışma kipi `LocalSemantic` olarak görünür. Web arayüzü bunu **Yerel gelişmiş dil anlama** etiketiyle gösterir.

## Doğrulama

- Yerel intent, bağlam, bileşik soru ve servis testleri dış bağlantı olmadan çalışır.
- Yazım hatalı ve doğal Türkçe corpus testleri bulunur.
- Harici sağlayıcı adaptörü varsayılan olarak kapalıdır; yerel sorgu çalışması için anahtar veya internet gerekmez.
- Veritabanı şeması değişmediği için migration gerekmez.
