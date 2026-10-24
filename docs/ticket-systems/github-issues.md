# GitHub Issues kurulumu

TickeTask, GitHub REST API'siyle seçili depoda issue açar. GitHub.com ve GitHub Enterprise Server desteklenir.

## Gereksinimler

- Issue açılacak depo. Depoda **Issues** sekmesi açık olmalıdır.
- Depoya yazma yetkisi olan kullanıcı veya makine hesabı.
- Fine-grained personal access token (önerilen) veya classic token.

## 1. Token oluşturun

### Fine-grained token (önerilen)

1. [GitHub fine-grained token sayfasını](https://github.com/settings/tokens?type=beta) açın.
2. **Generate new token** düğmesine basın.
3. **Repository access** bölümünde yalnızca ilgili depoyu seçin.
4. **Permissions → Repository permissions** altında:
   - **Issues:** Read and write
   - **Metadata:** Read-only (zorunlu, otomatik eklenir)
5. Tokenı oluşturup kopyalayın.

Kuruluşta SAML SSO zorunluysa tokenı oluşturduktan sonra kuruluş için **Configure SSO** ile yetkilendirin.

### Classic token (alternatif)

[Classic token sayfasında](https://github.com/settings/tokens) `repo` kapsamı (özel depo) veya yalnızca genel depo için `public_repo` kapsamı yeterlidir. Classic token tüm erişilebilir depolara geniş yetki verdiği için fine-grained token tercih edilmelidir.

## 2. GitHub Enterprise kullanıyorsanız API adresini girin

GitHub.com kullanıyorsanız bu alanı boş bırakın; TickeTask `https://api.github.com` adresini kullanır.

GitHub Enterprise Server için:

- Tokenı kendi GitHub Enterprise sunucunuzda **Settings → Developer settings → Personal access tokens** altından oluşturun.
- **Ayarlar → Entegrasyonlar → GitHub Issues** bölümünde **GitHub Enterprise API adresi** alanına sunucunuzun API kökünü yazın:

```text
https://ghe.sirket.com/api/v3
```

Adres HTTPS olmalıdır; `http://` ve içinde kullanıcı adı/parola bulunan adresler reddedilir.

> GitHub Enterprise Server sürümünüz fine-grained token desteklemiyorsa classic token kullanın. Kurumsal örnekte Issues özelliği yönetici tarafından kapatılmış olabilir.

## 3. TickeTask'a bilgileri girin

**Ayarlar → Entegrasyonlar → GitHub Issues** bölümünde tokenı (ve varsa Enterprise API adresini) girin, **Kaydet** ve **Bağlantıyı test et** düğmelerine basın. Test `/user` çağrısıyla tokenı doğrular.

## 4. Depoyu seçin

**Ayarlar → Proje yönetimi** bölümünde:

- Ticket sistemi: **GitHub Issues**
- GitHub deposu: `owner/repo`

Örnek: `https://github.com/acme/destek` için `acme/destek`. Tam GitHub URL'si de kabul edilir; TickeTask `owner/repo` biçimine normalleştirir. Depo adı büyük/küçük harfe duyarlıdır.

Kaydedip **Bağlantıyı test et** düğmesine basın. Test depoya erişimi ve **Issues** sekmesinin açık olup olmadığını denetler.

## Issue eşlemesi

| Ticket alanı | GitHub issue |
| --- | --- |
| Başlık | Issue başlığı |
| Açıklama | Issue gövdesi; öncelik, tür, vade, etiketler ve alt görevler Markdown metni olarak yazılır |
| Öncelik / tür / vade / etiketler | GitHub Issues'ta bu alanlar yerleşik olmadığı için gövdede görünür |
| Alt görevler | Gövdedeki "Yapılacaklar" listesine eklenir |
| Ek dosyalar | Desteklenmez |

TickeTask issue numarasını ve `html_url` adresini saklar. Issue başka bir kullanıcıya atanmaz, milestone veya proje panosuna eklenmez.

## Sınırlamalar

- Issue etiketleri, assignee, milestone ve GitHub Projects alanları otomatik doldurulmaz.
- Vade tarihi GitHub'da yerleşik bir issue alanı değildir; yalnızca gövdede yazar.
- Alt görevler gerçek GitHub task list öğeleri olarak değil, düz Markdown listesi olarak eklenir.
- Depoda Issues kapalıysa test ve ticket oluşturma başarısız olur.
- GitHub Enterprise Server'da API yolu `/api/v3` ile bitmelidir.

## Sorun giderme

- **401:** Token geçersiz veya süresi dolmuş. Yeni token oluşturun.
- **403:** Token kapsamı yetersiz, kuruluş SSO yetkilendirmesi eksik veya API hız sınırı aşıldı. Fine-grained token için Issues: Read and write ve depo erişimini kontrol edin.
- **404:** Depo adı yanlış veya token depoyu göremiyor. Özel depolarda tokenın o depoya erişimi olduğundan emin olun.
- **410 / Issues sekmesi kapalı:** Depo ayarlarından Issues özelliğini açın.
- **Enterprise bağlantısı kurulmuyor:** API adresini `https://sunucu/api/v3` biçiminde girin ve sunucunun TLS sertifikasının geçerli olduğunu kontrol edin.

## Ortam değişkenleri

```env
GITHUB_TOKEN=
# Yalnızca GitHub Enterprise için:
# GITHUB_API_URL=https://ghe.sirket.com/api/v3
```

## İlgili bağlantılar

- [Fine-grained personal access tokens](https://github.com/settings/tokens?type=beta)
- [Create an issue](https://docs.github.com/en/rest/issues/issues#create-an-issue)
- [GitHub REST API](https://docs.github.com/en/rest)
