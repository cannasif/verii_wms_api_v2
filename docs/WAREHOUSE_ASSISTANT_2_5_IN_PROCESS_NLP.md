# WMS AI Helper 2.5 — Sunucu İçi Dil Anlama

## Mimari kararı

Warehouse Assistant 2.5; OpenAI, başka bir dış API, Ollama, yerel LLM, embedding modeli
veya Python servisi kullanmaz. Dil çözümleme doğrudan .NET API süreci içinde çalışır.

Bu kararın sonuçları:

- İnternet veya ayrı model sunucusu gerekmez.
- Model indirme, GPU, yüksek RAM ve ısınma süresi yoktur.
- Kullanıcı sorusu üçüncü tarafa gönderilmez.
- Aynı soru aynı kurallarla tekrar edilebilir biçimde yorumlanır.
- Yeni bir operasyon dili eklemek için sözlük ve test eklemek gerekir; sistem sınırsız genel
  amaçlı sohbet modeli değildir.

## Çalışma sırası

1. Mesaj uzunluğu sınırlandırılır ve Türkçe karakterler güvenli biçimde normalize edilir.
2. Veri değiştirme talepleri daha sorgu planı oluşmadan reddedilir.
3. `stok`, `ürün`, `malzeme`, `cari`, `firma`, `seri`, `etiket`, `raf` gibi eş anlamlılar
   ve küçük yazım hataları değerlendirilir.
4. `hayır`, `değil`, `daha doğrusu`, `demek istediğim`, `yanlış yazdım` gibi düzeltmeler
   önceki konuşma bağlamının yanlış bölümünü temizler.
5. `o stok`, `bu seri`, `peki dün`, `hareketleri de` gibi kısa devam cümleleri yalnızca
   daha önce doğrulanmış konuşma bağlamını kullanır.
6. Aynı mesajdaki birbirinden farklı en fazla üç salt-okunur soru ayrılır.
7. Stok, cari, seri, barkod, plaka ve belge adayları mevcut veri çözümleme katmanında
   doğrulanır. Benzer birden fazla kayıt varsa kullanıcıdan seçim istenir.
8. Yetki, şube ve depo erişimi her alt sorgu için ayrıca kontrol edilir.

## Anlayabildiği konuşma biçimleri

### Doğal ve dolgu kelimeli anlatım

- `Abi şu 01/013 vardı ya, nerede duruyor ve elimizde kaç tane kalmış?`
- `ASD'den dün ne gelmiş, ne kadar almışız bir bakabilir misin?`
- `Şey bu DTG-1'in başından beri nerelere gittiğini görebiliyor muyuz?`

### Konuşma içinde düzeltme

- `Hayır seri değil; demek istediğim 01/013 malzemesi nerede?`
- `Yok plaka yanlış, 34 ABC 124 olacaktı; dünkü sac girişine bak.`
- `Dün değil, bugün yaptığım işlemleri göster.`

### Eksiltili devam sorusu

İlk soru: `01/013 nerede?`

Devam: `Peki dün hareketlerinde ne olmuş?`

Devam sorusu, doğrulanmış `01/013` stok bağlamını kullanır; kullanıcı başka bir stok
belirtirse yeni değer önceliklidir.

### Tek mesajda birden fazla sorgu

- `Bugün yaptığım işlemleri ve bana atanmış açık emirleri getir.`
- `01/013 malzemesi nerede ve hareketlerini ayrı ayrı göster.`

Her alt sorgu bağımsız yetki kontrolünden geçer ve sonuçlar tek yanıtta birleştirilir.

## Desteklenen ana konular

- Kullanıcının veya yetkisi varsa başka kullanıcıların depo aktiviteleri
- Seri bakiyesi ve ilk mal kabul geçmişi
- Stok, depo ve raf bakiyesi
- Barkod/etiket çözümleme
- Stok hareket geçmişi
- Atanmış açık emirler
- Cari ve tarih bazlı mal kabul analizi
- Sac araç girişleri ve plaka geçmişi
- Normal ve üretime transfer analizleri
- Vardiya özeti
- Operasyonel istisnalar
- Uçtan uca izlenebilirlik
- Belge/süreç engelleri
- Parametre etki açıklamaları

## Güvenlik sınırı

Assistant salt okunurdur. `sil`, `güncelle`, `onayla`, `ERP'ye gönder`, `emir ata`,
`stoktan düş` gibi talepler çalıştırılmaz. Dil motoru SQL üretmez ve repository'ye doğrudan
erişmez. Yalnızca izin verilen sorgu tipini ve parametre adaylarını çıkarır.

## Bilinçli sınırlar

- Genel kültür sohbeti veya serbest metin üretimi yapmaz.
- Tanımlanmamış yeni bir WMS operasyonunu kendiliğinden öğrenmez.
- Çok belirsiz bir mesajda tahmin ederek yanlış veri göstermek yerine yardım/seçim ister.
- Konuşma bağlamında yalnızca uygulamanın doğruladığı stok, seri, cari, plaka, belge ve tarih
  alanları taşınır; tüm mesaj geçmişi yeniden işlenmez.

## Yeni konu ekleme standardı

Yeni bir konu eklendiğinde birlikte teslim edilmesi gerekenler:

1. Intent ve salt-okunur uygulama sorgusu
2. Türkçe anahtar kelimeler, eş anlamlılar ve doğal konuşma örnekleri
3. Gerekli entity çıkarma/doğrulama kuralı
4. Yetki ve veri kapsamı kontrolü
5. Belirsizlik ve yanlış eşleşme testleri
6. En az bir konuşma devamı, düzeltme ve yazım hatası testi

