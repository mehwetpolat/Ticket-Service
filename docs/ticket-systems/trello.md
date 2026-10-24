# Trello kurulumu

TickeTask, Trello REST API'siyle seçili listeye kart açar. Kart adı ticket başlığı, açıklaması ise ortak ticket gövdesidir.

## Gereksinimler

- Bir Trello hesabı ve kart açılacak board/liste.
- Listeye kart ekleme yetkisi olan kullanıcı.
- API key ve kullanıcı tokenı.

## 1. API key ve token oluşturun

1. [Trello API key sayfasını](https://trello.com/app-key) açın. Sayfa Power-Up yönetim ekranına yönlendirirse **Trello Auth** sekmesinde **Generate a new API Key** düğmesini kullanın.
2. **Key** değerini kopyalayın.
3. Aynı ekrandaki **Token** bağlantısına tıklayın.
4. Token izin ekranında en az **read**, **write** ve **account** kapsamlarını onaylayın. TickeTask yalnızca kart oluşturur; yine de tokenın süresiz olması önerilir.
5. Gösterilen tokenı kopyalayın.

> Trello tokenı, hesabınızın board'larına erişim demektir. Tokenı yalnızca TickeTask'a verin; sızması durumunda Trello hesap ayarlarından iptal edin.

## 2. Liste ID'sini bulun

Hedef liste ID'si 24 karakterlik hexadecimal bir değerdir (ör. `5f1a2b3c4d5e6f7890abcdef`).

**Yöntem A — board JSON:**

1. Board'u tarayıcıda açın.
2. Adresin sonuna `.json` ekleyin: `https://trello.com/b/ABC123/board-adi.json`
3. Dönen JSON içinde `lists` dizisini bulun ve kartın açılacağı listenin `id` değerini alın.

**Yöntem B — Trello API:**

```bash
curl "https://api.trello.com/1/boards/BOARD_ID/lists?key=API_KEY&token=TOKEN&fields=id,name"
```

`BOARD_ID`, board JSON'undaki `id` alanıdır. Listenin `id` değerini kopyalayın.

## 3. TickeTask'a bilgileri girin

**Ayarlar → Entegrasyonlar → Trello** bölümünde:

| Alan | Değer |
| --- | --- |
| API key | Trello API key |
| Token | Oluşturduğunuz kullanıcı tokenı |

**Kaydet** düğmesine basın. Hedefi henüz girmeden de kimlik doğrulaması test edilebilir; hedef girildikten sonra test listeyi de doğrular.

## 4. Hedef listeyi seçin

**Ayarlar → Proje yönetimi** bölümünde:

- Ticket sistemi: **Trello**
- Trello liste ID: 24 karakterlik liste kimliği

Kaydedip **Bağlantıyı test et** düğmesine basın. Test; hesabı, listeyi ve listenin arşivli olup olmadığını denetler.

## Kart eşlemesi

| Ticket alanı | Trello kartı |
| --- | --- |
| Başlık | Kart adı |
| Açıklama | Kart açıklaması; öncelik, tür, vade, etiketler ve alt görevler buraya yazılır |
| Vade | Geçerli bir tarihse kartın `due` alanına `17:00 UTC` olarak yazılır |
| Öncelik / tür / etiketler | Trello'da yerel alan olmadığı için açıklamada metin olarak görünür |
| Alt görevler | Açıklamadaki "Yapılacaklar" listesine eklenir |
| Ek dosyalar | Desteklenmez |

Yeni kart listenin en altına eklenir. TickeTask kartın `id` ve `url` değerlerini saklar; ticket geçmişinden doğrudan Trello'ya gidilebilir.

## Sınırlamalar

- Trello checklist, etiket, üye veya ek dosya oluşturulmaz.
- Liste ID'si değiştirilirse Proje yönetimindeki hedef güncellenmelidir.
- Arşivlenmiş listeye kart açılmaz; test bunu önceden bildirir.
- Trello kartları için "backlog" veya sprint kavramı yoktur; kart seçili listede açılır.

## Sorun giderme

- **401:** API key/token çifti yanlış veya token iptal edilmiş. Yeni token oluşturun.
- **403:** Token read/write kapsamına sahip değil. Tokenı yeniden oluştururken write iznini onaylayın.
- **404:** Liste ID'si yanlış veya token bu board'a erişemiyor. Board üyeliğini kontrol edin.
- **Liste arşivlenmiş:** Trello'da listeyi yeniden açın veya açık bir liste seçin.
- **Vade görünmüyor:** Ticket tarihi `YYYY-MM-DD` biçiminde değilse Trello `due` alanı gönderilmez.

## Ortam değişkenleri

```env
TRELLO_API_KEY=
TRELLO_TOKEN=
```

## İlgili bağlantılar

- [Trello API key](https://trello.com/app-key)
- [Create card endpoint](https://developer.atlassian.com/cloud/trello/rest/api-group-cards/#api-cards-post)
- [Trello REST API](https://developer.atlassian.com/cloud/trello/rest/)
