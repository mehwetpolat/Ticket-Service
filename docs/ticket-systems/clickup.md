# ClickUp kurulumu

TickeTask, ClickUp API v2 ile seçili listede görev açar.

## Gereksinimler

- Bir ClickUp çalışma alanı (Workspace) ve görev açılacak liste.
- Listeye görev ekleme yetkisi olan kullanıcı.
- ClickUp personal API token.

## 1. Personal API token oluşturun

1. ClickUp'ta sağ üstteki avatara tıklayıp **Settings** bölümünü açın (doğrudan: [app.clickup.com/settings/apps](https://app.clickup.com/settings/apps)).
2. **Apps** sekmesine gidin.
3. **API Token** bölümünde **Generate** veya **Regenerate** düğmesine basın.
4. **Copy** ile tokenı kopyalayın. Token `pk_` ile başlar.

> Token, kullanıcının tüm workspace yetkileriyle çalışır. Yalnızca TickeTask'a verin; sızması durumunda aynı ekrandan **Regenerate** ile iptal edin.

## 2. Liste ID'sini bulun

ClickUp liste kimlikleri genellikle sayısaldır (ör. `901234567`).

1. ClickUp'ta hedef listeyi açın.
2. Adres çubuğuna bakın:

```text
https://app.clickup.com/1234567/v/li/901234567
```

`/li/` sonrasındaki değer liste ID'sidir. Adres farklı bir biçimdeyse (`/v/l/...` gibi) liste görünümünde **Copy link** ile paylaşılan bağlantıyı inceleyin; `/li/<ID>` bölümünü arayın.

Liste, bir Space veya Folder altında olabilir; TickeTask doğrudan liste ID'sini kullanır.

## 3. TickeTask'a bilgileri girin

**Ayarlar → Entegrasyonlar → ClickUp** bölümünde personal API tokenı girin, **Kaydet** ve **Bağlantıyı test et** düğmelerine basın. Test `/user` ile tokenı doğrular.

## 4. Listeyi seçin

**Ayarlar → Proje yönetimi** bölümünde:

- Ticket sistemi: **ClickUp**
- ClickUp liste ID: sayısal liste kimliği

Kaydedip bağlantı testini yeniden çalıştırın. Test listeye erişimi ve listenin arşivli olmadığını denetler.

## Görev eşlemesi

| Ticket alanı | ClickUp görevi |
| --- | --- |
| Başlık | Görev adı (`name`) |
| Açıklama | Görev açıklaması (`description`); öncelik, tür, vade, etiketler ve alt görevler buraya yazılır |
| Öncelik | Kritik → 1 (Urgent), Yüksek → 2 (High), Normal → 3 (Normal) |
| Vade | Geçerli bir tarihse `due_date` alanına milisaniye zaman damgası olarak yazılır (17:00 UTC) |
| Alt görevler | Açıklamadaki "Yapılacaklar" listesine eklenir |
| Ek dosyalar | Desteklenmez |

Görev seçili listenin varsayılan durumunda açılır. TickeTask görev ID'sini ve varsa ClickUp görev adresini saklar.

## Sınırlamalar

- ClickUp alt görevleri, etiketleri, atanan kişileri, özel alanları ve ek dosyaları oluşturulmaz.
- Görev durumu (status) seçilmez; listenin ilk/varsayılan durumu kullanılır.
- Liste ID'si değiştirilirse Proje yönetimindeki hedef güncellenmelidir.
- Arşivlenmiş listeye görev eklenemez.
- API hız sınırları plana göre değişir; `429` yanıtında kısa süre bekleyip yeniden deneyin.

## Sorun giderme

- **401:** Token geçersiz veya yeniden üretilmiş. Yeni tokenı kaydedin.
- **403:** Kullanıcının workspace/liste yetkisi yok. ClickUp'ta liste üyeliğini ve rol izinlerini kontrol edin.
- **404:** Liste ID'si yanlış veya token bu listeyi göremiyor.
- **Liste arşivlenmiş:** ClickUp'ta listeyi geri yükleyin veya açık bir liste seçin.
- **Öncelik beklenenden farklı:** ClickUp öncelikleri 1=Urgent, 2=High, 3=Normal, 4=Low şeklindedir; TickeTask üç kademeli önceliği bu ölçeğe eşler.
- **Vade görünmüyor:** Ticket tarihi `YYYY-MM-DD` biçiminde değilse `due_date` gönderilmez.

## Ortam değişkenleri

```env
CLICKUP_TOKEN=
```

## İlgili bağlantılar

- [ClickUp API token ayarları](https://app.clickup.com/settings/apps)
- [Create a task](https://developer.clickup.com/reference/createtask)
- [ClickUp API dokümanları](https://developer.clickup.com/docs/authentication)
