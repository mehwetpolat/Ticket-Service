# Jira Cloud kurulumu

TickeTask, Jira Cloud REST API v3 ile ticket açar. Jira Server / Data Center bu sürümde desteklenmez.

## Gereksinimler

- `https://sirket.atlassian.net` biçiminde bir Jira Cloud sitesi.
- Ticket açılacak proje ve bu projede **Create issues** izni olan bir kullanıcı.
- Alt görev kullanılacaksa projede alt görev türü, ek dosya kullanılacaksa ek yükleme izni.
- Proje anahtarı (ör. `IT`).

## 1. Kullanıcı API tokenı oluşturun

1. Tokenı oluşturacağınız kullanıcıyla [Atlassian API token sayfasını](https://id.atlassian.com/manage-profile/security/api-tokens) açın.
2. **Create API token** düğmesine basın.
3. Tokena tanımlayıcı bir ad verin (ör. `TickeTask`) ve oluşturun.
4. Tokenı hemen kopyalayın; sayfa kapandıktan sonra tekrar gösterilmez.

> Atlassian yönetim konsolundaki **organization API key** Jira ticket oluşturmak için kullanılamaz. Kullanıcının kendi API tokenı ve o kullanıcının e-posta adresi gerekir.

## 2. TickeTask'a bilgileri girin

**Ayarlar → Entegrasyonlar → Jira Cloud** bölümünde:

| Alan | Değer |
| --- | --- |
| Jira adresi | `https://sirket.atlassian.net` — yalnızca HTTPS ve site kökü. Sonundaki `/` normalleştirilir. |
| E-posta | Tokenı oluşturan Atlassian kullanıcısının e-posta adresi. |
| Kullanıcı API tokenı | 1. adımda oluşturduğunuz token. |

**Kaydet** düğmesine basın, ardından **Bağlantıyı test et** ile kimlik doğrulamasını doğrulayın.

## 3. Proje anahtarını girin

Jira'da projeyi açın. Adres çubuğunda:

```text
https://sirket.atlassian.net/jira/software/projects/IT/boards/1
```

görünüyorsa proje anahtarı `IT`'dir. **Ayarlar → Proje yönetimi** bölümünde ticket sistemini **Jira Cloud**, hedefi **IT** olarak seçin ve kaydedin. Ardından bağlantı testini yeniden çalıştırın; test projeye erişimi de denetler.

## TickeTask Jira'da ne oluşturur?

- **Ana ticket:** Projede öncelikli olarak tür eşlemesi yapılır: Hata → Bug/Incident, Talep → Service Request/Request, Yeni özellik → Story/Feature, Değişiklik → Change; uygun tür yoksa Task/Görev denenir.
- **Açıklama:** Düz metin, Jira ADF paragraflarına dönüştürülür. Öncelik, tür, vade, etiketler ve alt görevler açıklamada da görünür.
- **Öncelik:** Projede `priority` alanı varsa Highest/High/Medium eşlemesi uygulanır; alan yoksa atlanır.
- **Etiketler ve vade:** Projede ilgili alan varsa yazılır, yoksa ticket düşürülmeden atlanır.
- **Alt görevler:** Projede alt görev türü varsa gerçek Jira alt görevi olarak açılır. Yoksa alt görevler yalnızca açıklamada kalır.
- **Ek dosyalar:** Yalnızca web formundan yüklenen dosyalar Jira'ya eklenir. E-posta ekleri taşınmaz; yalnızca dosya adları açıklamada görünür.
- **Backlog:** Ticket, projenin backlog'unda oluşur; sprint planlaması Jira'da yapılır.

## Zorunlu alanlar

TickeTask ticket oluşturmadan önce Jira'nın create metadata'sını okur. `project`, `issuetype`, `summary`, `description` ve gerekliyse `reporter` dışında zorunlu bir alan varsa (ör. özel bir "Departman" alanı) ticket oluşturulamaz ve hata ticket geçmişinde gösterilir. Bu durumda alanı Jira projesinde isteğe bağlı yapın veya alanın varsayılan değerini tanımlayın.

Açıklama alanı Jira create ekranında yoksa (nadiren özel issue type şemalarında) TickeTask ticket açamaz.

## Sorun giderme

- **401:** E-posta/token çiftini kontrol edin. Yönetici API key'i değil, kullanıcı API tokenı kullanın.
- **403/404:** Proje anahtarını ve kullanıcının projeye erişimini kontrol edin. Kullanıcı projeye ekli değilse önce Jira'da ekleyin.
- **400 / zorunlu alan:** Hata mesajındaki alan adını Jira proje ayarlarında kontrol edin. Create ekranında zorunlu özel alanlar TickeTask tarafından doldurulmaz.
- **Ek yüklenemiyor:** Jira'da ek dosya özelliği kapalı veya dosya Jira'nın boyut sınırını aşıyor. Hata mesajı dosya adını belirtir.
- **Alt görev oluşmadı:** Projede alt görev türü yoktur; ana ticket açılır, alt görevler açıklamada kalır.

## Ortam değişkenleri

Panel yerine başlangıç değerleri vermek için:

```env
JIRA_BASE_URL=https://sirket.atlassian.net
JIRA_EMAIL=it@sirket.com
JIRA_API_TOKEN=
```

Panelden kaydedilen değerler ortam değişkenlerinden önceliklidir.

## İlgili bağlantılar

- [Atlassian API tokenları](https://id.atlassian.com/manage-profile/security/api-tokens)
- [Jira Cloud REST API v3](https://developer.atlassian.com/cloud/jira/platform/rest/v3/)
- [Create issue endpoint](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-issues/#api-rest-api-3-issue-post)
