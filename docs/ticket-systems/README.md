# Ticket sistemleri

TickeTask, aynı kurulum içinde beş farklı ticket sistemine kayıt açabilir:

| Hedef | Gerekli kimlik bilgisi | Proje yönetimindeki hedef | Alt görev | Ek dosya |
| --- | --- | --- | --- | --- |
| [Jira Cloud](jira.md) | Site adresi + e-posta + kullanıcı API tokenı | Proje anahtarı (`IT`) | Jira alt görevi | Web formundan yüklenir |
| [Trello](trello.md) | API key + token | Liste ID (24 karakter) | Açıklamaya eklenir | Desteklenmez |
| [GitHub Issues](github-issues.md) | Fine-grained veya classic token | `owner/repo` | Açıklamaya eklenir | Desteklenmez |
| [Asana](asana.md) | Personal access token | Proje GID | Açıklamaya eklenir | Desteklenmez |
| [ClickUp](clickup.md) | Personal API token | Liste ID | Açıklamaya eklenir | Desteklenmez |

Jira dışındaki hedeflerde ticket, kart, issue veya görev doğrudan oluşturulur; ayrıca bir "backlog" kavramı yoktur. Öncelik, tür, vade, etiket ve alt görev bilgileri TickeTask'ın ortak açıklama biçiminde gövdeye yazılır. Yalnızca Jira akışı Jira'nın yerel alanlarını, alt görev türünü ve ek dosyaları kullanır.

## Ortak kurulum akışı

1. **Entegrasyon bilgilerini girin.** Yönetim panelinde **Ayarlar → Entegrasyonlar** bölümünü açın, servisi seçin ve ilgili dokümandaki adımları izleyin. Bilgileri kaydettikten sonra **Bağlantıyı test et** düğmesine basın. Proje yönetiminde aynı sistem seçiliyse test hedefe erişimi de denetler.
2. **Hedefi seçin.** **Ayarlar → Proje yönetimi** bölümünde ticket sistemini ve hedef kimliğini girin. Bağlantı testi, kayıtlı hedefe erişimi de denetler.
3. **Kurulum sihirbazını doğrulayın.** **Ayarlar → Kurulum** ekranında entegrasyon ve hedef adımlarının tamamlandığını görün.
4. **Deneme modunda test edin.** Mail sistemi **Deneme** modundayken bir test e-postası işleyin; ardından canlı moda geçin.

Hedef değiştirildiğinde yalnızca yeni ticket'lar yeni sisteme gider. Geçmiş ticket kayıtları oluşturuldukları hedefi ve bağlantı adresini korur; başarısız bir ticket yeniden gönderilirken de kendi özgün hedefi kullanılır. Harici taraf kayıtlarında (ör. bir müşteri veya departman için) proje varsayılanından farklı bir hedef seçilebilir.

## Ortam değişkenleriyle ön yapılandırma

Entegrasyon bilgileri normalde şifrelenmiş olarak veritabanında saklanır. İsterseniz `.env` üzerinden başlangıç değerleri verebilirsiniz; yönetim panelinden kaydedilen değerler bunları geçersiz kılar.

| Hedef | Değişkenler |
| --- | --- |
| Jira | `JIRA_BASE_URL`, `JIRA_EMAIL`, `JIRA_API_TOKEN` |
| Trello | `TRELLO_API_KEY`, `TRELLO_TOKEN` |
| GitHub | `GITHUB_TOKEN`, isteğe bağlı `GITHUB_API_URL` |
| Asana | `ASANA_TOKEN` |
| ClickUp | `CLICKUP_TOKEN` |

Ortam değişkenlerini kullanmak zorunlu değildir; panelden giriş önerilir. Tokenları depoya veya `.env.example` dosyasına yazmayın.

## Güvenlik notları

- Tüm tokenlar AES-GCM ile şifrelenerek saklanır; panel token değerlerini geri göstermez.
- Özel API adresleri (GitHub Enterprise) yalnızca HTTPS olabilir; kullanıcı adı/parola içeren adresler reddedilir.
- Her hedef için yalnızca ticket oluşturmak için gereken en dar yetkiyi verin. Jira için yönetici API key'i değil, kullanıcı API tokenı kullanın.
- Tokenları düzenli olarak döndürün ve iptal edin; TickeTask'ta **Bağlantıyı kaldır** yalnızca kayıtlı bilgileri siler, sağlayıcıdaki tokenı iptal etmez.

## Sorun giderme

| Belirti | Olası neden ve çözüm |
| --- | --- |
| `401` / kimlik bilgileri reddedildi | Token yanlış, süresi dolmuş veya iptal edilmiş. Yeni token oluşturup yeniden kaydedin. |
| `403` / yetki yok | Hesabın hedef proje, liste veya depoya yazma yetkisi yok. Sağlayıcı tarafındaki üyeliği ve token kapsamlarını kontrol edin. |
| `404` / hedef bulunamadı | Hedef kimliği yanlış ya da token o hedefi göremiyor. Proje anahtarı, liste ID'si veya `owner/repo` değerini yeniden kontrol edin. |
| `410` / hedef kapalı | GitHub'da Issues sekmesi kapatılmış veya API sürümü desteklenmiyor olabilir. Depo ayarlarını kontrol edin. |
| `422` / alanlar reddedildi | Hedef sistem zorunlu bir alanı veya geçersiz değeri reddetti. Sağlayıcı yanıtındaki ayrıntıya bakın. |
| `429` / istek sınırı | Sağlayıcı hız sınırı. Kısa süre bekleyip yeniden deneyin. |
| Bağlantı testi başarılı, ticket açılmıyor | Hedef ayarının kaydedildiğinden ve mail kaydının canlı modda olduğundan emin olun. Ticket geçmişindeki hata mesajını inceleyin. |
