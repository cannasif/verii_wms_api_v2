# Warehouse Assistant 2.8 persona ve gerçek cevap değerlendirmesi

Tarih: 2026-08-17

Bu değerlendirme yalnızca intent sınıflandırmasını değil, gerçek `WarehouseAssistantService.AskAsync` cevabını ölçer. Bir vaka ancak intent, tipli sorgu planı ve sabit senaryodan dönen veri birlikte doğruysa geçer. Test artık herhangi bir yanlış vakada doğrudan başarısız olur.

## Sonuç

| Aşama | İşi bilen | İşi bilmeyen | Toplam |
|---|---:|---:|---:|
| İlk gerçek-cevap ölçümü | 17/20 | 5/20 | 22/40 (%55) |
| Korpus genişletildi, iyileştirme öncesi | 17/20 | 8/41 | 25/61 (%41) |
| Dil ve filtre iyileştirmeleri sonrası | 20/20 | 60/60 | 80/80 (%100) |
| Kibar/dolaylı yazma talepleri eklendikten sonra | 20/20 | 64/64 | 84/84 (%100) |

Son koşu: intent `84/84`, sorgu planı `84/84`, veri/cevap `84/84`, tam doğru `84/84`.

Bu oran sentetik sabit veri ve bilinen kabul korpusu için regresyon sonucudur; serbest kullanıcı dilinin tamamında %100 doğruluk iddiası değildir. Hatalı örnekler iyileştirmeyi yönlendirdiği için ayrıca bağımsız bir kör/holdout korpus tutulması önerilir.

## Yöntem ve sabit senaryo

- Üretim veritabanına bağlanılmadı; EF Core InMemory üzerinde sentetik veri kullanıldı.
- Dış AI servisi, migration veya üretim verisi kullanılmadı.
- Kullanıcının yetkili depoları `10 - Ana Depo` ve `20 - Yedek Depo`.
- Aynı şubedeki kullanıcıya kapalı depo `99`; başka şubedeki depo `77`.
- Depo 10 toplamı: fiziksel 110 AD, kullanılabilir 82 AD, rezerve 28 AD.
- `A01/R01-G01`: STK-A 70 AD ve STK-B 10 AD; toplam 80/100 AD dolu.
- `A01/R01-G02` boş; `KRN-01` karantina lokasyonu.
- Yetkili depolarda STK-A 125 AD, STK-B 10 AD; STK-C sıfır stok.
- Açık sayım `CNT-2026-001`; STK-A defter 70, sayılan 65, fark -5 AD.
- `PRJ-001`: planlanan 2, gerçekleşen 1; `OP-WELD` gecikmiş, malzeme eksiği ve kalite onayı bekliyor.

## Araştırma sonucu uygulanan dil yaklaşımı

.NET'in resmi önerileri doğrultusunda Unicode metin normalize ediliyor ve sembolik karşılaştırmalarda kültüre bağlı örtük eşleştirme yerine açık `StringComparison.Ordinal` kullanılıyor:

- [Best practices for comparing strings in .NET](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)
- [String.Normalize](https://learn.microsoft.com/en-us/dotnet/api/system.string.normalize?view=net-10.0)

Repo içi terimler de tarandı. `ambar`, `göz`, `raf gözü`, mal kabul, sayım ve transfer ifadelerinin proje içinde gerçek karşılıkları olduğu görüldü. Korpus şu kullanıcı davranışlarını kapsayacak şekilde büyütüldü:

- WMS terimini bilmeyen kullanıcı: `ambar`, `göz`, `mal`, `ne alemde`, `parça bekleyen`.
- Yazım ve boşluk hatası: `ürn`, `jenaratör`, `nerde`, `boşmu`, `onaylarmısın`.
- Aynı anlamın farklı söylenişi: `hiç kalmayan`, `stoğu bitmiş`, `yan yana göster`, `mukayese et`.
- Ekran arama dili: `hangi menü`, `nereden açacağım`, `nerdeydi`, `nasıl giderim`.
- Bağlam takip eden kısa soru: `Peki kapasitesi?`, `Peki toplam stok?`, `Peki parça bekleyen işi?`.
- Salt-okunur güvenlik sınırı: emir, kibar rica ve bitişik soru ekiyle yazılmış değişiklik talepleri.

## İşi bilen kullanıcı — gerçek cevaplar

| ID | Soru | Gerçek cevap özeti | Sonuç |
|---|---|---|---|
| EXP-01 | Kaç depo var? | Yetkili kapsamda depo 10 ve 20; `warehouseCount=2`. | Doğru |
| EXP-02 | 10 numaralı depoda hangi lokasyonlar var? | A01/R01-G01, A01/R01-G02 ve KRN-01. | Doğru |
| EXP-03 | 10 depodaki toplam fiziksel, rezerve ve kullanılabilir stok nedir? | Yalnız depo 10: 110 fiziksel, 28 rezerve, 82 kullanılabilir AD. | Doğru |
| EXP-04 | A01/R01-G01 lokasyonunda hangi ürünler var? | STK-A 70/55/15 ve STK-B 10/2/8 AD. | Doğru |
| EXP-05 | A01/R01-G01 kapasitesi ve doluluğu nedir? | Yalnız sorulan lokasyon; 80/100 AD dolu. | Doğru |
| EXP-06 | Karantina lokasyonları hangileri? | Yalnız KRN-01. | Doğru |
| EXP-07 | Stoku olmayan ürünler hangileri? | STK-C, 0 AD. | Doğru |
| EXP-08 | En fazla stoklu 2 ürünü göster | STK-A 125 AD, STK-B 10 AD. | Doğru |
| EXP-09 | Hammadde grubundaki stokları karşılaştır | Hammadde grubunda STK-A ve STK-B, doğru toplamlarla. | Doğru |
| EXP-10 | Kritik stok seviyesindeki ürünler hangileri? | Eşik verisi olmadığı için güvenilir liste üretilemeyeceğini açıkladı; veri uydurmadı. | Doğru |
| EXP-11 | Açık sayımlar hangileri? | Yalnız CNT-2026-001/InProgress; iptal sayım dışlandı. | Doğru |
| EXP-12 | Sayım farkı olan ürünler nelerdir? | STK-A: defter 70, sayılan 65, fark -5 AD. | Doğru |
| EXP-13 | Aktif jeneratör üretim projeleri hangileri? | Yalnız PRJ-001/InProgress. | Doğru |
| EXP-14 | Hangi jeneratör üretimleri malzeme bekliyor? | OP-WELD ve malzeme eksiği bilgisi. | Doğru |
| EXP-15 | Kalite kontrol bekleyen jeneratör üretimleri hangileri? | OP-WELD ve `Kalite: Pending`. | Doğru |
| EXP-16 | PRJ-001 için planlanan ve gerçekleşen jeneratör üretim miktarı nedir? | Planlanan 2, gerçekleşen 1. | Doğru |
| EXP-17 | Geciken jeneratör üretimler var mı? | Gecikmiş OP-WELD/InProgress. | Doğru |
| EXP-18 | Stok hareketleri ekranı nerede? | “Depo(Ambar) İşlemleri → Depo Yönetimi → Stok Hareketleri” yolunu söyledi; kartta görünür “Modülü Aç” bağlantısı var. | Doğru |
| EXP-19 | 99 depodaki toplam fiziksel stok nedir? | “Yetkili depo kapsamınızda eşleşen depo bulunamadı”; başka depo verisiyle genişletmedi. | Doğru |
| EXP-20 | WT-2026-001 transferini onayla | “Bu yardımcı salt okunurdur...” cevabı; plan `None`, hiçbir satır veya yazma işlemi yok. | Doğru |

## İşi bilmeyen kullanıcı — gerçek cevaplar

| ID | Soru | Gerçek cevap özeti | Sonuç |
|---|---|---|---|
| NOV-01 | Bizim kaç tane ambar var? | 2 yetkili depo. | Doğru |
| NOV-02 | 10 numaralı ambardaki gözleri göster | Depo 10'un üç lokasyonu. | Doğru |
| NOV-03 | Ürn STK-A nerde var? | STK-A, A01/R01-G01'de 70; kullanılabilir 55 AD. | Doğru |
| NOV-04 | A01/R01-G01 rafında ne var? | STK-A ve STK-B miktarları. | Doğru |
| NOV-05 | A01/R01-G02 boşmu? | A01/R01-G02 `Empty`. | Doğru |
| NOV-06 | A01/R01-G01 doluluk ne alemde? | Yalnız sorulan göz; 80/100 AD. | Doğru |
| NOV-07 | Hiç kalmayan mallar hangileri? | STK-C, 0 AD. | Doğru |
| NOV-08 | Depoda en çok hangi maldan var? | STK-A, STK-B, STK-C azalan fiziksel miktarla. | Doğru |
| NOV-09 | Kullanabileceğimiz en az 2 ürün ne? | Kullanılabilir miktarı en düşük STK-C ve STK-B. | Doğru |
| NOV-10 | Hammadde tarafındaki malları kıyasla | STK-A ve STK-B. | Doğru |
| NOV-11 | Devam eden sayım işleri? | CNT-2026-001/InProgress. | Doğru |
| NOV-12 | Sayımda tutmayan kalemler hangileri? | STK-A sayım farkı -5 AD. | Doğru |
| NOV-13 | Jeneratörde parça bekleyen işler hangileri? | Malzeme eksiği bulunan OP-WELD. | Doğru |
| NOV-14 | Geciken jenaratör işleri var mı? | Gecikmiş OP-WELD. | Doğru |
| NOV-15 | PRJ-001 ne alemde? | PRJ-001/InProgress. | Doğru |
| NOV-16 | Riskli seviyeye düşen ürünler hangileri? | Kritik eşik verisi olmadığını açıkladı; tahmin üretmedi. | Doğru |
| NOV-17 | Yeni mal girişi nereden açılıyor? | “Mal Kabul → Operasyon → Emir Oluştur” yolunu söyledi. | Doğru |
| NOV-18 | İki depo arasında ürün yollayacağım, nereden? | “Depo(Ambar) İşlemleri → Depolar Arası Transfer → Normal Transfer → Transfer Taslağı” yolunu söyledi. | Doğru |
| NOV-19 | Sayım sayfasını nereden bulurum? | “Depo(Ambar) İşlemleri → Depo Yönetimi → Sayım Yönetimi” yolunu söyledi. | Doğru |
| NOV-20 | Şu transferi hemen onayla | Salt-okunur ret; plan `None`, veri/yazma yok. | Doğru |
| NOV-21 | Ambarları sayar mısın? | 2 yetkili depo. | Doğru |
| NOV-22 | Kaç adet depo mevcut? | 2 yetkili depo. | Doğru |
| NOV-23 | 10 ambarda hangi raf gözleri var? | Depo 10'un üç lokasyonu. | Doğru |
| NOV-24 | A01/R01-G02 göz boşta mı? | A01/R01-G02 `Empty`. | Doğru |
| NOV-25 | Elde hiç olmayan ürünleri getir | STK-C, 0 AD. | Doğru |
| NOV-26 | En çok bulunan 3 ürünü sırala | STK-A, STK-B, STK-C azalan sırada. | Doğru |
| NOV-27 | Kullanılabilir miktarı en düşük 2 malzeme | STK-C ve STK-B artan kullanılabilir miktarla. | Doğru |
| NOV-28 | Hammadde ürünlerini yan yana göster | STK-A ve STK-B. | Doğru |
| NOV-29 | Sayım işi açık kalanlar | CNT-2026-001/InProgress. | Doğru |
| NOV-30 | Sayımda eksik fazla çıkanlar | STK-A sayım farkı -5 AD. | Doğru |
| NOV-31 | İptal sayımları gösterme | İptal kayıt dışlandı; CNT-2026-001 döndü. | Doğru |
| NOV-32 | Jenaratörde malzeme yüzünden duran işler | OP-WELD, malzeme eksiği. | Doğru |
| NOV-33 | Kontrolden onay bekleyen jeneratörler | OP-WELD, kalite `Pending`. | Doğru |
| NOV-34 | Jeneratörlerde kaç planladık kaç bitirdik? | PRJ-001 ve PRJ-DONE için planlanan/gerçekleşen değerler. | Doğru |
| NOV-35 | PRJ-001 işi nasıl gidiyor? | PRJ-001/InProgress. | Doğru |
| NOV-36 | Stokların giriş çıkışına nerden bakılır? | “Depo(Ambar) İşlemleri → Depo Yönetimi → Stok Hareketleri” yolunu söyledi. | Doğru |
| NOV-37 | Mal kabul sayfasını nereden açacağım? | “Mal Kabul → Operasyon → Emir Oluştur” yolunu söyledi. | Doğru |
| NOV-38 | Yeni sayım başlatmak için hangi sayfa? | “Depo(Ambar) İşlemleri → Depo Yönetimi → Sayım Yönetimi” yolunu söyledi. | Doğru |
| NOV-39 | Jeneratör projelerini hangi menüden bulurum? | “Üretim ve Kalite → Jeneratör Üretim → Planlama → Jeneratör Projeleri” yolunu söyledi. | Doğru |
| NOV-40 | Depolar arası transfer ekranına nasıl giderim? | “Depo(Ambar) İşlemleri → Depolar Arası Transfer → Normal Transfer → Transfer Taslağı” yolunu söyledi. | Doğru |
| NOV-41 | Yeni ürün kartı nereden ekleniyor? | Kartın ERP kaynaklı olduğunu ve “Entegrasyonlar → Stoklar” yolunu söyledi. | Doğru |
| NOV-42 | 10 nolu ambarın raflarını göster | Depo 10'un üç lokasyonu. | Doğru |
| NOV-43 | A01/R01-G01 içinde hangi mallar duruyor? | STK-A ve STK-B miktarları. | Doğru |
| NOV-44 | A01/R01-G02 boşta mı dolu mu? | A01/R01-G02 `Empty`. | Doğru |
| NOV-45 | Stoğu bitmiş ürünler neler? | STK-C, 0 AD. | Doğru |
| NOV-46 | İlk 2 en yüksek stok hangisi? | STK-A 125, STK-B 10 AD. | Doğru |
| NOV-47 | Hammadde grubunu mukayese et | STK-A ve STK-B. | Doğru |
| NOV-48 | Sayımda eksik çıkanları göster | STK-A sayım farkı -5 AD. | Doğru |
| NOV-49 | Jeneratör işinde materyal bekleyenler | OP-WELD, malzeme eksiği. | Doğru |
| NOV-50 | PRJ-001 hangi aşamada kaldı? | PRJ-001/InProgress. | Doğru |
| NOV-51 | Transfer sayfası nerdeydi? | “Depo(Ambar) İşlemleri → Depolar Arası Transfer → Normal Transfer → Transfer Taslağı” yolunu söyledi. | Doğru |
| NOV-52 | Mal kabulü hangi menüden yapıyorum? | “Mal Kabul → Operasyon → Emir Oluştur” yolunu söyledi. | Doğru |
| NOV-53 | Envanter sayma ekranı nerede? | “Depo(Ambar) İşlemleri → Depo Yönetimi → Sayım Yönetimi” yolunu söyledi. | Doğru |
| NOV-54 | Ürünlerin hareketine nereden bakacağım? | “Depo(Ambar) İşlemleri → Depo Yönetimi → Stok Hareketleri” yolunu söyledi. | Doğru |
| NOV-55 | A01/R01-G01 rafında ne var? | STK-A ve STK-B; takip konuşmasının bağlamı kaydedildi. | Doğru |
| NOV-56 | Peki kapasitesi ne kadar? | Önceki gözü korudu; A01/R01-G01 için 80/100 AD. | Doğru |
| NOV-57 | 10 numaralı depoda hangi lokasyonlar var? | Depo 10'un üç lokasyonu; takip bağlamı kaydedildi. | Doğru |
| NOV-58 | Peki toplam stok ne kadar? | Önceki depoyu korudu; yalnız depo 10 için 110/82/28 AD. | Doğru |
| NOV-59 | PRJ-001 jeneratör projesi ne durumda? | PRJ-001 durumu; takip bağlamı kaydedildi. | Doğru |
| NOV-60 | Peki parça bekleyen işi var mı? | Önceki projeyi korudu; PRJ-001 içindeki OP-WELD. | Doğru |
| NOV-61 | Şu transferi onaylayıver | Salt-okunur ret; plan `None`, veri/yazma yok. | Doğru |
| NOV-62 | Bu sayımı iptal edebilir misin? | Salt-okunur ret; plan `None`, veri/yazma yok. | Doğru |
| NOV-63 | STK-A kaydını güncelleyebilir misin? | Salt-okunur ret; plan `None`, veri/yazma yok. | Doğru |
| NOV-64 | Transferi onaylarmısın? | Bitişik soru ekine rağmen salt-okunur ret; plan `None`, veri/yazma yok. | Doğru |

## Başlangıçtaki hatalar ve giderilenler

- Açık `10 depodaki` ve lokasyon kodu filtreleri artık sessizce yok sayılmıyor.
- Yetkisiz/açıkça eşleşmeyen depo sorusu başka yetkili depoların toplamına genişletilmiyor.
- `ambar`, `göz`, günlük stok/sayım/jeneratör ifadeleri ve sık yazım hataları tanınıyor.
- Navigasyon soruları veri listesi yerine doğru ekran rotasını döndürüyor.
- Lokasyon, depo ve proje takip soruları konuşma bağlamını koruyor.
- Yazma talepleri emir veya kibar rica biçiminde olsa da `Unknown/None` planıyla reddediliyor; anlaşılır salt-okunur açıklama veriliyor.

## Kalan riskler ve sonraki test adımı

- Sözlük tabanlı yerel dil katmanı, korpusta olmayan yeni söyleyişleri kaçırabilir.
- Birbirine çok benzeyen gerçek depo/lokasyon/stok adları için belirsizlik senaryoları ayrıca genişletilmeli.
- Türkçe dışı kullanıcı dili ve karışık Türkçe/İngilizce cümleler ayrı persona seti olmalı.
- Bağımsız bir kişi tarafından, bu 84 soru görülmeden hazırlanmış en az 30 soruluk kör test korpusu eklenmeli.
- Gerçek kullanıcı telemetrisi kullanılacaksa kişisel/veri güvenliği kurallarıyla anonimleştirilmiş ve yalnız onaylı örnekler değerlendirilmelidir.

## Yeniden çalıştırma

Test dosyası: `tests/QueryTests/WarehouseAssistantPersonaEvaluationTests.cs`

Repo kökündeki `global.json` 10.0.302 istediği için bu makinede kurulu 10.0.400 SDK ile eşdeğer geçici SDK seçiminden çalıştırıldı. Test üretim veritabanına bağlanmaz.
