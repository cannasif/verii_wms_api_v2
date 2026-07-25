# V3RII WMS v2 Mimari ve Referans Notları

## Kaynak projeler

Tek referans konumu: `C:\Users\Can93\Desktop\V3rii`

- CRM referansı: `verii_crm_api` ve `verii_crm_web`
- Yeni WMS API: `verii_wms_api_v2`
- Yeni WMS web: `verii_wms_web_v2`
- Desktop altında ayrı `verii_wms_api` ve `verii_wms_web` klasörleri mevcut değil; bu nedenle eski depo yazılımı bu çalışma kapsamında ayrı bir kaynak olarak okunmayacak.

## CRM’den alınacak API dosya düzeni

Her feature mümkün olduğunca kendi sınırları içinde tutulacak:

```text
Modules/<Feature>
├── Api
├── Application
│   ├── Dtos
│   ├── Services
│   └── Mappings
├── Domain
│   ├── Entities
│   └── Enums
├── Infrastructure
│   ├── Persistence
│   │   └── Configurations
│   ├── Clients
│   ├── Options
│   └── Services
└── Localization
```

Ortak bileşenler feature içine kopyalanmayacak:

```text
Shared
├── Common
│   └── Application
│       ├── Dtos
│       ├── Mappings
│       ├── Common
│       ├── ApiResponse.cs
│       └── Paging
├── Infrastructure
│   ├── Persistence
│   ├── Services
│   │   └── Localization
│   └── Security
└── Host
    └── WebApi
```

## CRM NetsisRead referansı

CRM’de Netsis okuma işlemleri `Modules/NetsisIntegrations` altında tutuluyor.

Temel sınırlar:

- `Application/Services/INetsisReadService.cs`: uygulama sözleşmesi.
- `Application/Services/NetsisReadService.cs`: okuma iş kuralları ve sorgu orkestrasyonu.
- `Application/Dtos`: ERP tablolarının API’ye taşınacak DTO sözleşmeleri.
- `Api/NetsisReadController.cs`: HTTP endpoint’leri.
- `Infrastructure/Options`: ERP bağlantı ve Netsis ayarları.
- `Infrastructure/Persistence` veya ilgili read altyapısı: bağlantı/sorgu detayları.
- `Localization/NetsisIntegrationsLocalizationResource.cs`: başarı, hata ve validation mesajları.

Yeni WMS’te entity doğrudan controller’dan döndürülmeyecek. Akış:

```text
Controller -> INetsisReadService -> NetsisReadService -> read query/client -> DTO
```

İlk NetsisRead DTO/endpoint grupları:

- stok ve ürünler
- cari hesaplar
- depolar
- şubeler
- proje kodları
- özel kodlar
- döviz kurları
- stok grupları
- depo stok bakiyeleri
- sipariş/irsaliye üst ve satır kayıtları

## Web dosya düzeni

Web tarafında feature-first yapı kullanılacak:

```text
src
├── app
├── components
│   ├── layout
│   └── shared
├── features
│   └── <feature>
│       ├── api
│       ├── components
│       ├── hooks
│       ├── localization
│       ├── pages
│       ├── types
│       └── utils
├── layouts
├── routes
├── services
├── stores
├── styles
└── types
```

Auth, layout, sidebar, header ve Zustand store’ları ortak kabukta; WMS iş ekranları `features` altında tutulacak.

## Uygulama kuralları

1. Migration üretmek veya uygulamak bu aşamada yapılmayacak.
2. `appsettings.json` içinde veritabanı adı `V3RIIWMSV2` olacak; parola secret store/environment üzerinden sağlanacak.
3. Netsis bağlantısı `ErpConnection` üzerinden ayrı tutulacak.
4. Localization mesajları ilgili feature’ın `Localization` klasöründe duracak.
5. AutoMapper profilleri `Application/Mappings` altında feature bazında tutulacak.
6. Controller’lar ince kalacak; sorgu, validation ve mapping service katmanında olacak.
7. Eski WMS kodu bulunursa doğrudan kopyalanmayacak; önce DTO, iş kuralı ve bağlantı bağımlılıkları ayrıştırılacak.

## Sonraki uygulama sırası

1. CRM dosya düzenini v2 API’ye uyarlamak.
2. Identity/RII_USERS ve ortak persistence yapısını tamamlamak.
3. NetsisRead sözleşmesini ve ilk read-only DTO gruplarını taşımak.
4. Login/forgot-password ve web auth state’ini tamamlamak.
5. Layout/sidebar/header kabuğunu netleştirmek.
6. İlk WMS feature’ını bu kurallarla eklemek.
