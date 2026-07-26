# Netsis Yapılandırma Kodu Terminolojisi

## İş kavramı

Yapılandırma kodu, Netsis ürün konfigüratöründe bir ana stoğun varyantını veya seçilmiş özellik bileşimini ifade eder. Bağımsız bir stok kartı ya da yalnızca açıklama alanı değildir. Sipariş, stok hareketi, fiyat, üretim ve raporlama kayıtlarında stok koduyla birlikte taşınabilen bir iş boyutudur.

WMS içinde stok; depo, raf, lot ve seri boyutlarıyla izlenirken yapılandırma kodu da aynı stok için ayrı bakiye ve hareket ayrımı oluşturabilir. Bu nedenle:

- Kod, ilgili stokla uyumlu olmalıdır.
- Siparişten gelen kod operasyon satırına ve hareket anlık görüntüsüne taşınmalıdır.
- Barkod kuralında kullanılıyorsa çözümleme sonucu stokla birlikte doğrulanmalıdır.
- Bakiye, rezervasyon ve sevk seçimlerinde kod yok sayılmamalıdır.
- ERP aktarımında Netsis sözleşmesinin beklediği teknik alan kullanılmalıdır.

## Proje terminoloji kuralı

| Katman | Kullanılacak ad |
| --- | --- |
| Kullanıcı arayüzü (TR) | Yapılandırma Kodu |
| Kullanıcı arayüzü (EN) | Configuration Code |
| Yeni API rotası | `configuration-codes` |
| Yeni DTO ve servis adları | `ConfigurationCode` |
| Netsis SQL kolon/fonksiyon sınırı | `YAPKOD`, `YAPACIK` |
| Netsis REST JSON sınırı | `YapKod` |
| Mevcut WMS tablosu | `RII_YAP_CODE` |

`YapKod`, `YapCode` ve `RII_YAP_CODE` adları yalnız mevcut veritabanı, SQL fonksiyonu ve Netsis wire-contract uyumluluğu gereken sınırlarda korunur. Yeni iş kodunda ve kullanıcı metinlerinde kullanılmaz.

## Uyumluluk

- Yeni ERP ayna rotası: `POST /api/erp-mirror/configuration-codes/paged`
- Yeni senkronizasyon rotası: `POST /api/erp-mirror/sync/configuration-codes`
- Yeni doğrudan Netsis okuma rotası: `GET /api/netsis-read/configuration-codes`
- Eski `yap-codes` rotaları mevcut istemcileri kırmamak için geçici uyumluluk uçlarıdır.
- Eski web rotası `/erp/yapkodlar`, `/erp/configuration-codes` adresine yönlendirilir.

## Operasyonel doğrulama

Bir operasyon satırında yapılandırma kodu varsa API en az şu kontrolleri uygulamalıdır:

1. Kod aktif ERP ayna kaydında bulunmalıdır.
2. Kod bir stoğa bağlıysa satırdaki stokla eşleşmelidir.
3. Rezervasyon, toplama, transfer ve sevkte kaynak bakiye aynı yapılandırma kodundan seçilmelidir.
4. Mal kabul ve ambar girişinde kod hareket defterine değişmez anlık görüntü olarak yazılmalıdır.
5. Transfer kodu değiştirmez; yalnız depo ve raf boyutlarını değiştirir.
6. ERP REST yükünde alan adı Netsis uyumluluğu için `YapKod` olarak serileştirilmelidir.
