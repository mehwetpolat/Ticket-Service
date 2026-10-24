import { useCallback, useEffect, useState } from "react";
import { AlertCircle, BookOpen, CheckCircle2, ExternalLink, LoaderCircle, Mail, Plus, Save, Server, Trash2 } from "lucide-react";
import { api } from "../lib/api";
import type { Mailbox, MailConfig, MailPollStatus, MailProvider } from "../lib/types";

const providerLabels: Record<MailProvider, string> = {
  imap: "IMAP · parola",
  microsoftgraph: "Microsoft 365 · Graph",
  gmail: "Google Workspace · Gmail",
};

const providerShort: Record<MailProvider, string> = {
  imap: "IMAP",
  microsoftgraph: "Microsoft 365",
  gmail: "Google Workspace",
};

function providerDefaults(provider: MailProvider) {
  switch (provider) {
    case "microsoftgraph": return { host: "graph.microsoft.com", port: 443, secure: true, mailbox: "inbox" };
    case "gmail": return { host: "gmail.googleapis.com", port: 443, secure: true, mailbox: "INBOX" };
    default: return { host: "", port: 993, secure: true, mailbox: "INBOX" };
  }
}

function blankMailbox(provider: MailProvider = "imap"): Mailbox {
  return {
    id: typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : `mail-${Date.now()}`,
    provider, label: "", user: "", password: "",
    tenantId: "", clientId: "", clientSecret: "", refreshToken: "", serviceAccountJson: "",
    ...providerDefaults(provider),
  };
}

function emptyConfig(): MailConfig {
  return { enabled: false, dryRun: true, pollSeconds: 60, importExisting: false, maxPerPoll: 25, mailboxes: [blankMailbox()] };
}

function mailboxSummary(mailbox: Mailbox): string {
  if (mailbox.provider === "imap") return mailbox.host ? `${mailbox.user} · ${mailbox.host}:${mailbox.port}` : "Henüz yapılandırılmadı";
  if (mailbox.provider === "microsoftgraph") return mailbox.user ? `${mailbox.user} · Microsoft Graph` : "Henüz yapılandırılmadı";
  return mailbox.user ? `${mailbox.user} · Gmail API` : "Henüz yapılandırılmadı";
}

function hasMailboxCredentials(mailbox: Mailbox): boolean {
  if (mailbox.provider === "imap") return Boolean(mailbox.password || mailbox.hasPassword);
  if (mailbox.provider === "microsoftgraph") {
    return Boolean(mailbox.tenantId.trim() && mailbox.clientId.trim() && (mailbox.clientSecret || mailbox.hasClientSecret));
  }
  return Boolean(mailbox.serviceAccountJson.trim() || mailbox.hasServiceAccount ||
    (mailbox.clientId.trim() && (mailbox.clientSecret || mailbox.hasClientSecret) && (mailbox.refreshToken || mailbox.hasRefreshToken)));
}

export function MailSettings({ onNotice, onError, onChanged }: { onNotice: (value: string) => void; onError: (value: string) => void; onChanged?: () => void }) {
  const [form, setForm] = useState<MailConfig>(emptyConfig);
  const [status, setStatus] = useState<MailPollStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [guide, setGuide] = useState<MailProvider>("imap");

  const refreshStatus = useCallback(async () => {
    try {
      const result = await api.mailConfig();
      setStatus(result.status);
      return result;
    } catch (error) {
      onError(error instanceof Error ? error.message : "Mail ayarları yüklenemedi.");
      return null;
    }
  }, [onError]);

  useEffect(() => {
    let active = true;
    async function initial() {
      const result = await refreshStatus();
      if (!active) return;
      if (result?.config) {
        setForm(result.config);
        const primaryProvider = result.config.mailboxes[0]?.provider;
        if (primaryProvider) setGuide(primaryProvider);
      }
      setLoaded(true);
    }
    void initial();
    const timer = window.setInterval(() => { if (active) void refreshStatus(); }, 8000);
    return () => { active = false; window.clearInterval(timer); };
  }, [refreshStatus]);

  function updateMailbox(id: string, patch: Partial<Mailbox>) {
    setForm((previous) => ({ ...previous, mailboxes: previous.mailboxes.map((mailbox) => mailbox.id === id ? { ...mailbox, ...patch } : mailbox) }));
  }

  function changeProvider(mailbox: Mailbox, provider: MailProvider) {
    setGuide(provider);
    if (mailbox.provider === provider) return;
    updateMailbox(mailbox.id, {
      provider,
      ...providerDefaults(provider),
      password: "", hasPassword: false,
      tenantId: "", clientId: "", clientSecret: "", hasClientSecret: false,
      refreshToken: "", hasRefreshToken: false,
      serviceAccountJson: "", hasServiceAccount: false,
      hasCredentials: false,
    });
  }

  function chooseGuide(provider: MailProvider) {
    setGuide(provider);
    if (form.mailboxes.length === 1 && form.mailboxes[0].provider !== provider) {
      changeProvider(form.mailboxes[0], provider);
    }
  }

  function addMailbox() {
    if (form.mailboxes.length >= 10) { onError("En fazla 10 mail hesabı eklenebilir."); return; }
    setForm((previous) => ({ ...previous, mailboxes: [...previous.mailboxes, blankMailbox()] }));
  }

  function removeMailbox(id: string) {
    if (form.mailboxes.length <= 1) { onError("En az bir mail hesabı kalmalı."); return; }
    if (!window.confirm("Bu mail hesabı ayarlardan kaldırılsın mı? Geçmiş kayıtlar silinmez.")) return;
    setForm((previous) => ({ ...previous, mailboxes: previous.mailboxes.filter((mailbox) => mailbox.id !== id) }));
  }

  async function save() {
    setBusy(true); onError("");
    try {
      const invalid = form.mailboxes.find((mailbox) =>
        !mailbox.user.trim() || !mailbox.mailbox.trim() || (mailbox.provider === "imap" && !mailbox.host.trim()));
      if (invalid) throw new Error("Her mail hesabı için hesap ve gelen kutusu alanlarını doldurun.");
      const missingCredentials = form.mailboxes.find((mailbox) => form.enabled && !hasMailboxCredentials(mailbox));
      if (missingCredentials) throw new Error(`${missingCredentials.user} için ${providerLabels[missingCredentials.provider]} kimlik bilgilerini girin.`);
      const missingTenant = form.mailboxes.find((mailbox) => form.enabled && mailbox.provider === "microsoftgraph" && !mailbox.tenantId.trim());
      if (missingTenant) throw new Error(`${missingTenant.user} için Microsoft 365 kiracı kimliği girin.`);
      await api.saveMailConfig(form);
      const result = await api.mailConfig();
      if (result.config) setForm(result.config);
      setStatus(result.status);
      onNotice("Mail ayarları kaydedildi. Poller birkaç saniye içinde bağlantıyı kontrol edecek.");
      onChanged?.();
    } catch (error) { onError(error instanceof Error ? error.message : "Mail ayarları kaydedilemedi."); }
    finally { setBusy(false); }
  }

  const connected = Object.values(status?.mailboxes ?? {}).filter((item) => item.state === "connected").length;
  const errors = Object.values(status?.mailboxes ?? {}).filter((item) => item.state === "error").length;

  return <>
    <section className="panel setup-panel">
      <div className="section-heading"><Server size={21}/><div><h2>IT mail kutusu bağlantısı</h2><p>IT biriminin gelen kutusunu ekleyin. IMAP parolası, Microsoft 365 Graph veya Google Workspace Gmail API kullanabilirsiniz. Parola ve OAuth bilgileri AES-GCM ile şifreli saklanır ve ekranda gösterilmez.</p></div></div>

      <div className="mail-guide">
        <div className="mail-guide-head">
          <BookOpen size={18}/>
          <div><strong>Kurulum yönergesi</strong><small>Hesabınızın türüne göre adımları izleyin. Tek mail hesabınız varsa sekme seçimi aşağıdaki formu da o sağlayıcıya geçirir.</small></div>
        </div>
        <div className="mail-guide-tabs" role="tablist" aria-label="Mail sağlayıcısı kurulum yönergesi">
          {(Object.keys(providerShort) as MailProvider[]).map((provider) => (
            <button key={provider} type="button" role="tab" aria-selected={guide === provider} className={guide === provider ? "active" : ""} onClick={() => chooseGuide(provider)}>{providerShort[provider]}</button>
          ))}
        </div>
        {form.mailboxes.length > 1 && <small className="mail-guide-scope">Birden fazla hesap var. Sağlayıcıyı her hesap kartındaki <strong>Sağlayıcı</strong> alanından ayrı seçin; sekmeler yalnızca yönergeyi değiştirir.</small>}
        {guide === "imap" && <div className="mail-guide-body">
          <ol>
            <li>Mail sunucunuzdan IMAP adresi, port ve hesap bilgilerini alın. Çoğu kurulumda <code>993</code> + TLS kullanılır.</li>
            <li>Hesapta iki adımlı doğrulama varsa sağlayıcının verdiği uygulama parolasını oluşturun.</li>
            <li>Hesap kartında sağlayıcıyı <strong>IMAP</strong> seçin; sunucu, kullanıcı, parola, gelen kutusu (<code>INBOX</code>) ve bağlantı türünü doldurun.</li>
            <li><strong>Mail ayarlarını kaydet</strong> düğmesine basın ve <strong>Bağlantı durumu</strong> kartında <strong>Bağlı</strong> yazmasını bekleyin.</li>
          </ol>
          <p className="mail-guide-note">STARTTLS seçildiğinde sunucu STARTTLS desteklemek zorundadır; şifrelenmemiş bağlantıya geri düşülmez. Self-signed sertifikalar reddedilir. Microsoft 365 veya Google Workspace temel kimlik doğrulamayı kapattıysa IMAP yerine Microsoft 365 veya Google Workspace sağlayıcısını kullanın.</p>
        </div>}
        {guide === "microsoftgraph" && <div className="mail-guide-body">
          <ol>
            <li><a href="https://portal.azure.com" target="_blank" rel="noreferrer">Azure portal</a> → <strong>Microsoft Entra ID</strong> → <strong>Uygulama kayıtları</strong> → <strong>Yeni kayıt</strong> ile tek kiracılı uygulama oluşturun.</li>
            <li><strong>API izinleri</strong> → <strong>İzin ekle</strong> → <strong>Microsoft Graph</strong> → <strong>Uygulama izinleri</strong> → <code>Mail.Read</code> ekleyin ve <strong>Yönetici onayı ver</strong> düğmesine basın.</li>
            <li><strong>Sertifikalar ve sırlar</strong> → <strong>Yeni istemci sırrı</strong> oluşturun. Değer yalnızca bir kez gösterilir; hemen kopyalayın.</li>
            <li><strong>Genel bakış</strong> sayfasından <strong>Dizin (kiracı) ID</strong> ve <strong>Uygulama (istemci) ID</strong> değerlerini kopyalayın.</li>
            <li>Hesap kartında sağlayıcıyı <strong>Microsoft 365 · Graph</strong> seçin; posta kutusu adresi, kiracı ID, istemci ID, istemci sırrı ve klasörü doldurun. Gelen kutusu için <code>inbox</code>, gönderilenler için <code>sentitems</code> gibi iyi bilinen klasör adlarını kullanın. Özel klasörlerde görünen ad değil <strong>klasör ID'si</strong> gerekir.</li>
            <li>Kaydedin ve <strong>Bağlantı durumu</strong> kartında <strong>Bağlı</strong> yazmasını bekleyin.</li>
          </ol>
          <p className="mail-guide-note">İstemci sırlarının son kullanma tarihi vardır (genellikle 6-24 ay); süresi dolunca yeni sır oluşturup kaydedin. Uygulama izni kiracıdaki tüm posta kutularına erişebilir. Yalnızca IT kutusuna izin vermek için Exchange Online'da application access policy tanımlayın. <a href="https://learn.microsoft.com/exchange/security-and-compliance/application-access-policy" target="_blank" rel="noreferrer">Belge <ExternalLink size={12}/></a></p>
        </div>}
        {guide === "gmail" && <div className="mail-guide-body">
          <h4>Seçenek A · Hizmet hesabı (önerilen)</h4>
          <ol>
            <li><a href="https://console.cloud.google.com" target="_blank" rel="noreferrer">Google Cloud Console</a>'da proje oluşturun ve <strong>Gmail API</strong>'yi etkinleştirin.</li>
            <li><strong>IAM ve Yönetici</strong> → <strong>Hizmet hesapları</strong> → yeni hesap oluşturun. Hizmet hesabının <strong>Gelişmiş ayarları</strong>nda <strong>Google Workspace alan genelinde yetkilendirme</strong> seçeneğini etkinleştirin; <strong>Anahtarlar</strong> → <strong>Anahtar ekle</strong> → <strong>JSON</strong> ile anahtarı indirin.</li>
            <li>Google Yönetici konsolu → <strong>Güvenlik</strong> → <strong>API erişimi</strong> → <strong>Alan genelinde yetkilendirme</strong> → hizmet hesabının istemci ID'sini ve <code>https://www.googleapis.com/auth/gmail.readonly</code> kapsamını ekleyin. Kaydettikten sonra 5-10 dakika bekleyin.</li>
            <li>Hesap kartında <strong>Google Workspace · Gmail</strong> seçin; taklit edilecek posta kutusu, JSON dosyasının tamamı ve etiket alanlarını doldurun. Sistem etiketi (<code>INBOX</code>) veya özel etiketin görünen adını/kimliğini (<code>Label_...</code>) girebilirsiniz; görünen adlar otomatik çevrilir.</li>
          </ol>
          <h4>Seçenek B · OAuth yenileme belirteci</h4>
          <ol>
            <li>Google Cloud Console'da <strong>OAuth izin ekranı</strong> (Internal) ve <strong>OAuth istemcisi</strong> (Web uygulaması) oluşturun. Yönlendirme adresi olarak <code>https://developers.google.com/oauthplayground</code> ekleyin.</li>
            <li><a href="https://developers.google.com/oauthplayground" target="_blank" rel="noreferrer">OAuth 2.0 Playground</a>'da kendi istemci bilgilerinizi girin; <code>https://www.googleapis.com/auth/gmail.readonly</code> kapsamıyla yetkilendirip refresh token alın.</li>
            <li>İstemci ID, istemci sırrı ve refresh token alanlarını doldurun.</li>
          </ol>
          <p className="mail-guide-note">Uygulama e-postayı yalnızca okur; silmez ve okundu işaretlemez. <code>INBOX</code> etiketi arşivlenmiş postaları kapsamaz. Hizmet hesabı için alan genelinde yetkilendirme hem Cloud Console'da açılmalı hem de Workspace yöneticisi tarafından istemci ID'siyle onaylanmalıdır. OAuth izin ekranı <strong>Testing</strong> modundaysa yenileme belirteci 7 gün sonra geçersiz olur; kurum içi kullanımda <strong>Internal</strong> seçin.</p>
        </div>}
      </div>

      <div className="mail-mode-grid">
        <label className="control"><span>Mail kanalı</span><select value={form.enabled ? "on" : "off"} onChange={(event) => setForm({ ...form, enabled: event.target.value === "on" })}><option value="off">Kapalı</option><option value="on">Açık · arka planda çalışır</option></select></label>
        <label className="control"><span>Gönderim modu</span><select value={form.dryRun ? "dry" : "live"} onChange={(event) => setForm({ ...form, dryRun: event.target.value === "dry" })}><option value="dry">Deneme · ticket sistemine gönderme</option><option value="live">Canlı · ticket aç</option></select></label>
        <label className="control"><span>Kontrol aralığı (saniye)</span><input type="number" min="15" max="3600" value={form.pollSeconds} onChange={(event) => setForm({ ...form, pollSeconds: Number(event.target.value) })}/></label>
      </div>

      <div className="mail-account-list">
        {form.mailboxes.map((mailbox, index) => {
          const mailboxStatus = status?.mailboxes?.[mailbox.id];
          return <article className="mail-account-card" key={mailbox.id}>
            <div className="mail-account-head">
              <div><span className="mail-account-icon"><Mail size={17}/></span><div><strong>{mailbox.label || mailbox.user || `Mail hesabı ${index + 1}`}</strong><small>{mailboxSummary(mailbox)}</small></div></div>
              <div className="mail-account-actions">
                {mailboxStatus && <span className={mailboxStatus.state === "connected" ? "connection good" : mailboxStatus.state === "error" ? "connection bad" : "connection warn"}>{mailboxStatus.state === "connected" ? <CheckCircle2 size={14}/> : <AlertCircle size={14}/>} {mailboxStatus.state === "connected" ? "Bağlı" : mailboxStatus.state === "error" ? "Hata" : "Bekliyor"}</span>}
                <button className="icon-button danger-action" aria-label="Mail hesabını kaldır" onClick={() => removeMailbox(mailbox.id)}><Trash2 size={15}/></button>
              </div>
            </div>
            <div className="form-row two">
              <label className="control"><span>Görünen ad (isteğe bağlı)</span><input value={mailbox.label} placeholder="IT Destek" onChange={(event) => updateMailbox(mailbox.id, { label: event.target.value })}/></label>
              <label className="control"><span>Sağlayıcı</span><select value={mailbox.provider} onChange={(event) => changeProvider(mailbox, event.target.value as MailProvider)}><option value="imap">IMAP · kullanıcı adı + parola</option><option value="microsoftgraph">Microsoft 365 · Graph API</option><option value="gmail">Google Workspace · Gmail API</option></select></label>
              <label className="control"><span>Mail hesabı / kullanıcı adı</span><input autoComplete="username" value={mailbox.user} placeholder="it@sirket.com" onChange={(event) => updateMailbox(mailbox.id, { user: event.target.value.trim() })}/></label>

              {mailbox.provider === "imap" ? <>
                <label className="control"><span>IMAP sunucusu</span><input value={mailbox.host} placeholder="imap.sirket.com" onChange={(event) => updateMailbox(mailbox.id, { host: event.target.value.trim() })}/></label>
                <label className="control"><span>IMAP parolası</span><input type="password" autoComplete="new-password" value={mailbox.password} placeholder={mailbox.hasPassword ? "Kaydedildi · değiştirmek için yeni parola girin" : "Mail hesabının parolası veya uygulama parolası"} onChange={(event) => updateMailbox(mailbox.id, { password: event.target.value })}/></label>
                <label className="control"><span>Gelen kutusu</span><input value={mailbox.mailbox} placeholder="INBOX" onChange={(event) => updateMailbox(mailbox.id, { mailbox: event.target.value })}/></label>
                <label className="control"><span>Bağlantı türü</span><select value={mailbox.secure ? "tls" : "starttls"} onChange={(event) => updateMailbox(mailbox.id, { secure: event.target.value === "tls", port: event.target.value === "tls" ? 993 : 143 })}><option value="tls">TLS · genellikle 993</option><option value="starttls">STARTTLS · genellikle 143</option></select></label>
                <label className="control"><span>Port</span><input type="number" min="1" max="65535" value={mailbox.port} onChange={(event) => updateMailbox(mailbox.id, { port: Number(event.target.value) })}/></label>
              </> : null}

              {mailbox.provider === "microsoftgraph" ? <>
                <label className="control"><span>Kiracı kimliği (tenant ID)</span><input value={mailbox.tenantId} placeholder="contoso.onmicrosoft.com veya GUID" onChange={(event) => updateMailbox(mailbox.id, { tenantId: event.target.value.trim() })}/></label>
                <label className="control"><span>Uygulama (client) kimliği</span><input value={mailbox.clientId} placeholder="Azure uygulama kimliği" onChange={(event) => updateMailbox(mailbox.id, { clientId: event.target.value.trim() })}/></label>
                <label className="control"><span>İstemci parolası</span><input type="password" autoComplete="new-password" value={mailbox.clientSecret} placeholder={mailbox.hasClientSecret ? "Kaydedildi · değiştirmek için yeni değer girin" : "Azure uygulama istemci parolası"} onChange={(event) => updateMailbox(mailbox.id, { clientSecret: event.target.value })}/></label>
                <label className="control"><span>Klasör</span><input value={mailbox.mailbox} placeholder="inbox" onChange={(event) => updateMailbox(mailbox.id, { mailbox: event.target.value })}/></label>
                <p className="setting-help">Azure AD'de uygulama kaydı oluşturun, <strong>Mail.Read</strong> uygulama iznini ekleyin ve yönetici onayı verin. İyi bilinen klasör adları (<code>inbox</code>, <code>sentitems</code>) küçük harfe çevrilir; özel klasörlerde klasör ID'si girin. İstemci sırrının süresini takip edin. Uygulama, <code>{mailbox.user || "hesap"}</code> posta kutusuna erişecek.</p>
              </> : null}

              {mailbox.provider === "gmail" ? <>
                <label className="control"><span>Etiket / klasör</span><input value={mailbox.mailbox} placeholder="INBOX" onChange={(event) => updateMailbox(mailbox.id, { mailbox: event.target.value })}/></label>
                <label className="control"><span>OAuth istemci kimliği</span><input value={mailbox.clientId} placeholder="Google OAuth client ID" onChange={(event) => updateMailbox(mailbox.id, { clientId: event.target.value.trim() })}/></label>
                <label className="control"><span>OAuth istemci parolası</span><input type="password" autoComplete="new-password" value={mailbox.clientSecret} placeholder={mailbox.hasClientSecret ? "Kaydedildi · değiştirmek için yeni değer girin" : "Google OAuth client secret"} onChange={(event) => updateMailbox(mailbox.id, { clientSecret: event.target.value })}/></label>
                <label className="control"><span>Yenileme belirteci (refresh token)</span><input type="password" autoComplete="new-password" value={mailbox.refreshToken} placeholder={mailbox.hasRefreshToken ? "Kaydedildi · değiştirmek için yeni belirteç girin" : "offline erişimle alınmış refresh token"} onChange={(event) => updateMailbox(mailbox.id, { refreshToken: event.target.value })}/></label>
                <label className="control"><span>Hizmet hesabı JSON (alternatif)</span><textarea rows={3} value={mailbox.serviceAccountJson} placeholder={mailbox.hasServiceAccount ? "Kaydedildi · değiştirmek için yeni JSON yapıştırın" : "Alan genelinde yetkilendirilmiş hizmet hesabı JSON'u"} onChange={(event) => updateMailbox(mailbox.id, { serviceAccountJson: event.target.value })}/></label>
                <p className="setting-help">Etikete sistem etiketi (<code>INBOX</code>) veya özel etiketin görünen adı/kimliği (<code>Label_...</code>) yazılabilir. OAuth istemcisi için <code>access_type=offline</code> ile yenileme belirteci alın; <strong>Testing</strong> modunda belirteç 7 gün sonra geçersiz olur, <strong>Internal</strong> kullanın veya yeniden yetkilendirin. Alternatif olarak hizmet hesabı JSON'u yapıştırın; Cloud Console'da alan genelinde yetkilendirme açık ve Workspace yöneticisi tarafından onaylı olmalıdır. Bu durumda OAuth istemci alanları boş bırakılabilir.</p>
              </> : null}
            </div>
            {mailboxStatus?.message && <p className={mailboxStatus.state === "error" ? "item-error" : "setting-help"}>{mailboxStatus.message}{mailboxStatus.checkedAt ? ` · ${new Date(mailboxStatus.checkedAt).toLocaleString("tr-TR")}` : ""}</p>}
          </article>;
        })}
      </div>

      <div className="setting-actions">
        <button className="secondary" onClick={addMailbox} disabled={busy || form.mailboxes.length >= 10}><Plus size={16}/>Mail hesabı ekle</button>
        <button className="primary" onClick={() => void save()} disabled={busy || !loaded}><Save size={15}/>{busy ? "Kaydediliyor…" : "Mail ayarlarını kaydet"}</button>
      </div>

      <div className="mail-advanced-grid">
        <label className="control checkbox-control"><input type="checkbox" checked={form.importExisting} onChange={(event) => setForm({ ...form, importExisting: event.target.checked })}/><span><strong>Mevcut e-postaları da işle</strong><small>Varsayılan kapalıdır: bu durumda yalnızca yeni e-postalar işlenir. Açarsanız kutudaki eski e-postalar da sırayla işlenir; canlı modda her biri seçili hedefte ticket açabilir.</small></span></label>
        <label className="control"><span>Tur başına en fazla e-posta</span><input type="number" min="1" max="100" value={form.maxPerPoll} onChange={(event) => setForm({ ...form, maxPerPoll: Number(event.target.value) })}/><small>Büyük kutularda ilk taramayı parçalara böler.</small></label>
      </div>

      <p className="setting-help"><strong>Önemli:</strong> Önce deneme modunda bir test e-postası gönderin ve aşağıdaki Mail işlem geçmişinde sonucu görün. Canlı moda geçtiğinizde yalnızca bundan sonra gelen uygun e-postalar otomatik ticket olur. Microsoft 365 için Graph uygulama izni, Google Workspace için OAuth yenileme belirteci veya hizmet hesabı yetkilendirmesi gerekir; kurum mail yöneticinizle doğrulayın.</p>
    </section>
    <section className="panel setup-panel">
      <div className="section-heading"><Server size={21}/><div><h2>Bağlantı durumu</h2><p>Poller sık sık uyanır; mail kontrolü seçtiğiniz aralıkta yapılır ve ayar değişikliğinde hemen tetiklenir. Arayüz kapalı olsa da kontrol ve ticket açma devam eder.</p></div></div>
      <div className="setup-status-row">
        <span className={status?.state === "connected" ? "connection good" : status?.state === "error" ? "connection bad" : "connection warn"}>
          {status?.state === "connected" ? <CheckCircle2 size={15}/> : status?.state === "error" ? <AlertCircle size={15}/> : <LoaderCircle size={15}/>} {status?.message ?? "Poller durumu bekleniyor"}
        </span>
        {status?.checkedAt && <small>Son kontrol: {new Date(status.checkedAt).toLocaleString("tr-TR")}</small>}
        <small>{connected} bağlı · {errors} hatalı · {form.mailboxes.length} hesap</small>
      </div>
    </section>
  </>;
}
