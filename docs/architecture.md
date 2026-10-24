# Mimari

TickeTask, ASP.NET Core Minimal API üzerinde çalışan tek süreçli bir uygulamadır. React arayüzü aynı süreçten statik dosya olarak sunulur; mail poller ve model indirme işleri de süreç içinde arka plan görevleri olarak yürür.

## Katmanlar

```text
TickeTask.Web
  ├─ Api/ApiEndpoints.*.cs        HTTP sözleşmeleri (partial)
  ├─ Security/                    oturum, Origin/CSRF, güvenlik başlıkları
  ├─ wwwroot/                     derlenmiş React SPA
  └─ Program.cs                   host, migration, middleware
TickeTask.Infrastructure
  ├─ Persistence/                 EF Core, repository, unit of work, migration
  ├─ Services/                    ayarlar, mail, ticket, model, setup
  ├─ Integrations/                Jira, Trello, GitHub, Asana, ClickUp
  ├─ Mail/                        IMAP, Microsoft Graph ve Gmail sağlayıcıları + poller
  └─ Security/                    PBKDF2, AES-GCM, token, hız sınırı
TickeTask.Application
  ├─ Abstractions/                servis ve repository sözleşmeleri
  ├─ Dtos/                        API veri sözleşmeleri
  ├─ Tickets/                     analiz, taslak biçimlendirme, hedef kataloğu
  └─ Common/                      ApiException, JSON ayarları
TickeTask.Domain
  └─ Entities, enums, iş kuralları
```

Bağımlılık yönü `Web → Infrastructure → Application → Domain` şeklindedir. Application katmanı EF Core veya HTTP istemcilerini bilmez.

## API sözleşmeleri

Tüm uç noktalar `/api` altında toplanmıştır ve özgün TypeScript sürümünün JSON sözleşmelerini korur. Hatalar `{ "error": "..." }` biçiminde, Türkçe mesajlarla döner. Liste uç noktaları `{ items: [...] }` veya alan adına göre `{ people: [...] }` gibi sarmalayıcılar kullanır.

| Grup | Dosya |
| --- | --- |
| Oturum | `ApiEndpoints.Session.cs` |
| Kullanıcılar | `ApiEndpoints.Users.cs` |
| Yapılandırma ve sağlık | `ApiEndpoints.Config.cs` |
| Proje | `ApiEndpoints.Project.cs` |
| Müşteri, kişi, cihaz | `ApiEndpoints.Customers.cs`, `People.cs`, `Devices.cs` |
| Mesajlar | `ApiEndpoints.Messages.cs` |
| Ticket ve yayınlama | `ApiEndpoints.Tickets.cs` |
| Analiz | `ApiEndpoints.Analyze.cs` |
| Ayarlar ve entegrasyon testi | `ApiEndpoints.Settings.cs` |
| Mail | `ApiEndpoints.Mail.cs` |
| Model | `ApiEndpoints.Model.cs` |
| Kurulum durumu | `ApiEndpoints.Setup.cs` |
| Webhook | `ApiEndpoints.Webhooks.cs` |

Tarayıcıdan gelen mutasyon isteklerinde `Origin` ve `Sec-Fetch-Site` doğrulanır. API istemcileri bearer token kullanır.

## Kalıcılık

- EF Core 8 + SQLite. Varsayılan dosya `data/ticketask.db`.
- Migration'lar açılışta `Database.MigrateAsync()` ile uygulanır.
- Repository'ler `IUnitOfWork` üzerinden tek `DbContext` paylaşır.
- Entity'lerde tarihler Unix saniye (`long`) olarak tutulur; API DTO'larında ISO tarih kullanılmaz, özgün sözleşme korunur.
- Enum'lar özel dönüştürücüyle küçük harfli anahtarlar olarak saklanır (`dry_run`, `jira`, `critical`).

## Kimlik doğrulama ve güvenlik

- İlk yönetici, `.ticketask-runtime/admin-password` dosyasındaki kurulum şifresiyle oluşturulur.
- Şifreler PBKDF2-SHA256 (210.000 tur, rastgele tuz) ile saklanır.
- Oturum token'ı istemciye `HttpOnly`, `SameSite=Strict` çerezle verilir; veritabanında SHA-256 özeti tutulur.
- Entegrasyon anahtarları, mail parolaları ve OAuth/hizmet hesabı sırları AES-GCM ile şifrelenir. Anahtar `master-key` dosyasından veya `APP_MASTER_KEY` değişkeninden gelir.
- Hız sınırlayıcı süreç içidir. Çok örnekli dağıtımda merkezi bir çözüm gerekir.
- Model adresi yalnızca özel ağ/loopback host'larına izin verir; yönlendirmeler kapalıdır.

## Ticket akışı

1. Mesaj web formu, e-posta veya webhook ile alınır.
2. Yerel LLM (`/api/chat`) mesajı ve doğrulanan envanter/otomasyon bağlamını değerlendirir; yapılandırılmış JSON taslak üretir. Kural tabanlı yedek analiz yoktur; model erişilemezse işlem hata olarak kaydedilir ve yeniden denenir.
3. Taslak hedef sistemin biçimine dönüştürülür; açıklamanın sonuna bağlam ve özgün kaynak mesaj eklenir.
4. Yerel ticket durumu, atama ve ekip notları harici sistemden bağımsız tutulur.
5. Deneme modunda yalnızca yerel kayıt tutulur; canlı modda hedef API'ye gönderilir.
6. Harici kimlik ve URL ticket'a yazılır; hata durumunda `failed` durumu ve mesaj saklanır, yeniden gönderim desteklenir.

Jira ek dosya ve alt görev desteği sunar. Trello, GitHub Issues, Asana ve ClickUp için zengin alanlar açıklama gövdesine yazılır.

## Mail ingestion

- `ImapMailPoller` bir `BackgroundService`'tir; hesapları periyodik olarak tarar.
- Her kutu için son işlenen UID durumu `MAIL_STATE_DIR` altında saklanır.
- İlk taramada mevcut mesajlar varsayılan olarak atlanır (`importExisting` kapalı).
- `mail_ingestions` tablosu mesaj kimliği, deneme sayısı, hata, otomatik/toplu e-posta işareti ve ticket bağlantısını tutar; başarısız kayıtlar üstel geri çekilmeyle yeniden denenir.
- Gövde HTML'den metne çevrilir; ek dosya adları kaydedilir, içerik indirilmez.
- Gönderen adresi envanter kişileriyle; mesaj gövdesi ve kişinin cihazları cihazlarla eşleştirilir. Eşleşen kişi/cihaz ticket alanlarına ve açıklama bağlamına yazılır.

## Model depolama

`IModelControlClient` iki uygulamaya sahiptir:

- `LocalModelStorageService` (varsayılan): model köklerini, klasör gezintisini, seçimi, Ollama indirmesini ve doğrudan indirme yedeğini yönetir.
- `ModelControlClient`: `MODEL_CONTROL_URL` tanımlıysa eski harici Node servisiyle konuşur.

Yerel uygulama seçili konumun `models` klasörüne `MODEL_CONFIG_DIR/models` bağlantısı kurar. Windows'ta sembolik bağlantı izni yoksa junction denenir. İndirme işi durumu `download-job.json` dosyasında tutulur; süreç yeniden başlarsa yarım iş `error` olarak işaretlenir.

## Test stratejisi

- `ApplicationTests`: analiz, biçimlendirme, hedef kataloğu, ayar doğrulama.
- `PersistenceTests`: repository davranışları ve ilişkisel sayımlar.
- `SecurityTests`: şifre, token, şifreleme, hız sınırı.
- `MailIngestionTests`: katı LLM akışı, deneme modu, tekrar işleme, ticket üretimi.
- `ModelStorageTests`: model kökleri, klasör oluşturma, seçim kalıcılığı, yol güvenliği.
- `ApiIntegrationTests`: oturum, CSRF, CRUD, yerel ticket akışı ve hata sözleşmeleri.

Testler SQLite in-memory ve `WebApplicationFactory` kullanır; harici ağ çağrısı yapılmaz. API testlerinde `ILocalLlmClient` sahte bir istemciyle değiştirilir; böylece analiz sözleşmesi Ollama olmadan doğrulanır.
