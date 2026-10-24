# Asana kurulumu

TickeTask, Asana REST API'siyle seçili projede görev açar.

## Gereksinimler

- Bir Asana çalışma alanı ve görev açılacak proje.
- Projeye üye olan ve görev oluşturma yetkisi bulunan kullanıcı.
- Asana personal access token (PAT).

## 1. Personal access token oluşturun

1. Asana'da [developer console / My Apps](https://app.asana.com/0/my-apps) sayfasını açın.
2. **Personal access tokens** bölümünde **Create new token** düğmesine basın.
3. Tokena bir ad verin (ör. `TickeTask`) ve oluşturun.
4. Tokenı hemen kopyalayın.

> PAT, tokenı oluşturan kullanıcının yetkileriyle çalışır. Bu kullanıcının projeye üye olduğundan ve görev oluşturabildiğinden emin olun. Kuruluş politikanız PAT kullanımını kısıtlıyorsa bir hizmet hesabı için token oluşturmayı değerlendirin.

## 2. Proje GID'sini bulun

Asana proje kimlikleri sayısal GID'lerdir (ör. `1201234567890123`).

1. Asana'da projeyi açın.
2. Adres çubuğuna bakın:

```text
https://app.asana.com/0/1201234567890123/1209876543210987
```

Buradaki ilk sayı (`/0/` sonrası) proje GID'sidir. İkinci sayı açık görevin GID'sidir; proje GID'siyle karıştırmayın.

Proje listesindeki bir projeye tıkladığınızda adres yalnızca `/0/<proje-GID>` biçiminde de olabilir.

## 3. TickeTask'a bilgileri girin

**Ayarlar → Entegrasyonlar → Asana** bölümünde personal access tokenı girin, **Kaydet** ve **Bağlantıyı test et** düğmelerine basın. Test `/users/me` ile tokenı doğrular.

## 4. Projeyi seçin

**Ayarlar → Proje yönetimi** bölümünde:

- Ticket sistemi: **Asana**
- Asana proje ID: sayısal GID

Kaydedip bağlantı testini yeniden çalıştırın. Test projeye erişimi ve projenin arşivli olmadığını denetler.

## Görev eşlemesi

| Ticket alanı | Asana görevi |
| --- | --- |
| Başlık | Görev adı (`name`) |
| Açıklama | Görev notları (`notes`); öncelik, tür, vade, etiketler ve alt görevler buraya yazılır |
| Vade | Geçerli bir tarihse `due_on` alanına `YYYY-MM-DD` olarak yazılır |
| Öncelik / tür / etiketler | Asana'da yerel alan olmadığı için notlarda metin olarak görünür |
| Alt görevler | Notlardaki "Yapılacaklar" listesine eklenir |
| Ek dosyalar | Desteklenmez |

Görev seçili projeye eklenir; atanan kişi, bölüm veya etiket atanmaz. TickeTask görev GID'sini ve varsa `permalink_url` adresini saklar.

## Sınırlamalar

- Asana alt görevleri, etiketleri, özel alanları, bölümleri ve ek dosyaları oluşturulmaz.
- Görev bir kullanıcıya atanmaz; Asana projesindeki varsayılan atama kuralları geçerli olabilir.
- Arşivlenmiş projelere görev eklenemez.
- Proje GID'si yerine görev GID'si girilirse test 404 veya hata verir.

## Sorun giderme

- **401:** PAT geçersiz veya iptal edilmiş. Yeni token oluşturun.
- **403:** Kullanıcının projede görev oluşturma yetkisi yok. Proje üyeliğini ve çalışma alanı izinlerini kontrol edin.
- **404:** Proje GID'si yanlış veya kullanıcı projeyi göremiyor.
- **Proje arşivlenmiş:** Asana'da projeyi arşivden çıkarın veya açık bir proje seçin.
- **Vade görünmüyor:** Ticket tarihi `YYYY-MM-DD` biçiminde değilse `due_on` gönderilmez.

## Ortam değişkenleri

```env
ASANA_TOKEN=
```

## İlgili bağlantılar

- [Asana kimlik doğrulama](https://developers.asana.com/docs/authentication)
- [Create a task](https://developers.asana.com/reference/createtask)
- [Asana API referansı](https://developers.asana.com/reference)
