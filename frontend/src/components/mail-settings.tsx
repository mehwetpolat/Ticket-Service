import { useCallback, useEffect, useState } from "react";
import { AlertCircle, CheckCircle2, LoaderCircle, Mail, Plus, Save, Server, Trash2 } from "lucide-react";
import { api } from "../lib/api";
import type { Mailbox, MailConfig, MailPollStatus } from "../lib/types";

function blankMailbox(): Mailbox {
  return {
    id: typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : `mail-${Date.now()}`,
    label: "", host: "", port: 993, secure: true, user: "", password: "", mailbox: "INBOX",
  };
}

function emptyConfig(): MailConfig {
  return { enabled: false, dryRun: true, pollSeconds: 60, importExisting: false, maxPerPoll: 25, mailboxes: [blankMailbox()] };
}

export function MailSettings({ onNotice, onError, onChanged }: { onNotice: (value: string) => void; onError: (value: string) => void; onChanged?: () => void }) {
  const [form, setForm] = useState<MailConfig>(emptyConfig);
  const [status, setStatus] = useState<MailPollStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);

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
      if (result?.config) setForm(result.config);
      setLoaded(true);
    }
    void initial();
    const timer = window.setInterval(() => { if (active) void refreshStatus(); }, 8000);
    return () => { active = false; window.clearInterval(timer); };
  }, [refreshStatus]);

  function updateMailbox(id: string, patch: Partial<Mailbox>) {
    setForm((previous) => ({ ...previous, mailboxes: previous.mailboxes.map((mailbox) => mailbox.id === id ? { ...mailbox, ...patch } : mailbox) }));
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
      const invalid = form.mailboxes.find((mailbox) => !mailbox.host.trim() || !mailbox.user.trim() || !mailbox.mailbox.trim());
      if (invalid) throw new Error("Her mail hesabı için sunucu, hesap ve gelen kutusu alanlarını doldurun.");
      const missingPassword = form.mailboxes.find((mailbox) => !mailbox.password && !mailbox.hasPassword);
      if (missingPassword) throw new Error(`${missingPassword.user} için IMAP parolası girin.`);
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
      <div className="section-heading"><Server size={21}/><div><h2>IT mail kutusu bağlantısı</h2><p>IT biriminin gelen kutusunu ekleyin. Birden fazla kutu veya farklı konumlar için hesap ekleyebilirsiniz. Parolalar AES-GCM ile şifreli saklanır ve ekranda gösterilmez.</p></div></div>

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
              <div><span className="mail-account-icon"><Mail size={17}/></span><div><strong>{mailbox.label || mailbox.user || `Mail hesabı ${index + 1}`}</strong><small>{mailbox.host ? `${mailbox.user} · ${mailbox.host}:${mailbox.port}` : "Henüz yapılandırılmadı"}</small></div></div>
              <div className="mail-account-actions">
                {mailboxStatus && <span className={mailboxStatus.state === "connected" ? "connection good" : mailboxStatus.state === "error" ? "connection bad" : "connection warn"}>{mailboxStatus.state === "connected" ? <CheckCircle2 size={14}/> : <AlertCircle size={14}/>} {mailboxStatus.state === "connected" ? "Bağlı" : mailboxStatus.state === "error" ? "Hata" : "Bekliyor"}</span>}
                <button className="icon-button danger-action" aria-label="Mail hesabını kaldır" onClick={() => removeMailbox(mailbox.id)}><Trash2 size={15}/></button>
              </div>
            </div>
            <div className="form-row two">
              <label className="control"><span>Görünen ad (isteğe bağlı)</span><input value={mailbox.label} placeholder="IT Destek" onChange={(event) => updateMailbox(mailbox.id, { label: event.target.value })}/></label>
              <label className="control"><span>IMAP sunucusu</span><input value={mailbox.host} placeholder="imap.sirket.com" onChange={(event) => updateMailbox(mailbox.id, { host: event.target.value.trim() })}/></label>
              <label className="control"><span>Mail hesabı / kullanıcı adı</span><input autoComplete="username" value={mailbox.user} placeholder="it@sirket.com" onChange={(event) => updateMailbox(mailbox.id, { user: event.target.value.trim() })}/></label>
              <label className="control"><span>IMAP parolası</span><input type="password" autoComplete="new-password" value={mailbox.password} placeholder={mailbox.hasPassword ? "Kaydedildi · değiştirmek için yeni parola girin" : "Mail hesabının parolası veya uygulama parolası"} onChange={(event) => updateMailbox(mailbox.id, { password: event.target.value })}/></label>
              <label className="control"><span>Gelen kutusu</span><input value={mailbox.mailbox} placeholder="INBOX" onChange={(event) => updateMailbox(mailbox.id, { mailbox: event.target.value })}/></label>
              <label className="control"><span>Bağlantı türü</span><select value={mailbox.secure ? "tls" : "starttls"} onChange={(event) => updateMailbox(mailbox.id, { secure: event.target.value === "tls", port: event.target.value === "tls" ? 993 : 143 })}><option value="tls">TLS · genellikle 993</option><option value="starttls">STARTTLS · genellikle 143</option></select></label>
              <label className="control"><span>Port</span><input type="number" min="1" max="65535" value={mailbox.port} onChange={(event) => updateMailbox(mailbox.id, { port: Number(event.target.value) })}/></label>
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

      <p className="setting-help"><strong>Önemli:</strong> Önce deneme modunda bir test e-postası gönderin ve aşağıdaki Mail işlem geçmişinde sonucu görün. Canlı moda geçtiğinizde yalnızca bundan sonra gelen uygun e-postalar otomatik ticket olur. Microsoft 365 ve Google Workspace çoğu kurulumda normal parola yerine uygulama parolası veya OAuth ister; IMAP erişimini kurum mail yöneticinizle doğrulayın.</p>
    </section>
    <section className="panel setup-panel">
      <div className="section-heading"><Server size={21}/><div><h2>Bağlantı durumu</h2><p>Poller 15 saniyede bir uyanır; mail kontrolü seçtiğiniz aralıkta yapılır. Arayüz kapalı olsa da kontrol ve ticket açma devam eder.</p></div></div>
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
