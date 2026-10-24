# Güvenlik modeli

TickeTask'ın yerel Docker kurulumu mesajları, ticket taslaklarını, modeli ve veritabanını kurum bilgisayarında tutar. Ollama servisi Docker iç ağına bağlıdır; ana bilgisayara ayrı bir Ollama portu açılmaz. Mesaj analizi harici bir yapay zekâ API'sine gönderilmez.

Model konumu seçicisi yalnızca Docker kurulumunda önceden bağlanan iki model köküne erişebilir; sunucunun tüm diskleri konteynere açılmaz. Model yönetimi uygulama sürecinin içindedir ve ayrı bir yönetim portu yayınlamaz. Yol doğrulaması `..`, sembolik bağlantı ve kök dışına çıkma girişimlerini reddeder. İndirilen modeller seçili kökte tutulur; başka konuma geçildiğinde eski modeller otomatik silinmez.

## Ağ ve dağıtım

Varsayılan `compose.yaml` uygulama portunu yalnızca `127.0.0.1:8787` adresine bağlar. Şirket ağındaki diğer bilgisayarlardan erişim gerekiyorsa `TICKETASK_BIND_ADDRESS=0.0.0.0` yapılabilir; bu durumda bile 8787 portunu internete veya güvenilmeyen ağlara açmayın. Gerçek personel ve cihaz verisi için kurumun TLS sertifikasıyla HTTPS ters vekili kullanın. Ters vekil arkasında `APP_PUBLIC_ORIGIN` ve `TRUST_PROXY=1` ayarlarını yapın; `TRUST_PROXY` yalnızca istekleri güvenilir bir vekil iletiyorsa açılmalıdır.

Uygulama; güvenlik başlıkları, Origin/`Sec-Fetch-Site` denetimi ve istek gövdesi sınırları uygular. API mutasyonları aynı origin'den gelmelidir.

## Kimlik doğrulama ve oturumlar

Yerel kurulumun ilk yönetici şifresi rastgele oluşturulur; Docker'da `data/secrets/admin-password`, doğrudan `dotnet run` sırasında `.ticketask-runtime/admin-password` dosyasına yazılır. İlk girişte yönetici hesabı açılır; sonraki çalışan kayıtları yönetici onayı bekler. Şifreler ayrı tuzla PBKDF2-SHA256 kullanılarak saklanır. Oturum çerezi `HttpOnly` ve `SameSite=Strict` kullanır; HTTPS arkasında `Secure` olarak gönderilir. Oturum belirteci veritabanında yalnızca SHA-256 özetiyle tutulur ve şifre değişiminde diğer oturumlar kapatılır.

Entegrasyon anahtarları ve mail parolaları veritabanına AES-GCM ile şifrelenerek yazılır. `master-key` kaybolursa bu değerler kurtarılamaz; anahtarı ve veritabanını birlikte, şifreli olarak yedekleyin. `APP_MASTER_KEY` ortam değişkeni verilirse dosyadaki anahtar yerine kullanılır.

Giriş, kayıt, şifre değiştirme, analiz, bağlantı testi ve webhook uç noktalarında hız sınırı vardır. Hız sınırlayıcı süreç içidir; çok örnekli bir dağıtımda merkezi bir çözüm gerekir. Webhook imzaları sabit zamanlı karşılaştırılır; `INBOUND_WEBHOOK_TOKEN`, `WHATSAPP_VERIFY_TOKEN` ve `WHATSAPP_APP_SECRET` tanımlı değilse ilgili uç nokta isteği kabul etmez.

## E-posta ve ticket oluşturma

IT mail kutusu yönetici tarafından yapılandırılır. İzin listesi yalnızca `From` adresini denetler; kurum mail sunucusunda SPF, DKIM ve DMARC doğrulamasını açık tutun, aksi halde gönderen adresi taklit edilebilir. Boş, yalnızca selamlama/imza içeren, otomatik yanıt, bülten ve teslim edilemeyen posta niteliğindeki mesajlar da yerel modele değerlendirme için gönderilir; ticket açılıp açılmayacağına model karar verir. Kural tabanlı yedek analiz yoktur. E-posta içeriği prompt injection uyarısıyla ve güvenilmez veri olarak işlenir; envanter bağlamı ayrı ve doğrulanmış bölüm olarak eklenir.

Varsayılan gönderim modu denemedir; seçili ticket sistemine kayıt gönderilmez. Yönetici canlı moda geçtiğinde izinli gönderenlerden gelen uygun e-postalar arka planda otomatik olarak seçili hedefte ticket'a dönüşür. Canlı moda geçmeden önce deneme kayıtlarını inceleyin. Jira, Trello, GitHub, Asana ve ClickUp bağlantıları yönetici tarafından yapılandırılır; bağlantı testi ilgili hizmete bir doğrulama isteği yollar.

IMAP poller uygulama içinde çalışır ve durum dosyalarını `MAIL_STATE_DIR` altında tutar. Kutu boşaltılmaz, mesaj silinmez; okundu işaretleme yapılandırmaya bağlıdır.

## Bilinen sınırlamalar

Bu sürümde yönetici ve çalışan rolleri vardır; ayrıntılı işlem denetim günlüğü, MFA ve merkezi kimlik sağlayıcı entegrasyonu yoktur. Uygulama tek kiracılıdır. Mail kimlik doğrulaması yalnızca IMAP kullanıcı adı + parola/uygulama parolası destekler; OAuth2 yoktur. Bu nedenle uygulamayı internete doğrudan açmayın; kurum içi ağda da erişimi ters vekil ve ağ kurallarıyla sınırlandırın.

Model indirme işlemi Ollama API'sine ve isteğe bağlı olarak `registry.ollama.ai` adresine erişir. Kurum ağınız dışarıya çıkmıyorsa `MODEL_REGISTRY_URL` boş bırakılabilir; yalnızca dahili Ollama akışı kullanılır. Model dosyaları doğrulanmış SHA-256 özetleriyle indirilir.

## Bildirim

Güvenlik açığını herkese açık issue yerine GitHub deposundaki **Security → Report a vulnerability** (Security Advisories) kanalından bildirin. Her kurum, token yetkilerini asgari düzeyde tutmalı ve düzenli yenilemelidir.
