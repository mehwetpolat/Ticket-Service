import { useCallback, useEffect, useState } from "react";
import { AlertCircle, Check, Download, Globe2, LoaderCircle, Save, Server, ShieldCheck, Trash2 } from "lucide-react";
import { api } from "../lib/api";
import type { Config, MailDomain, MailSender, ModelStorage, ProjectConfig, SetupStatus, Target, UserRecord } from "../lib/types";
import { destinationError, normalizeDestination, TICKET_TARGETS, targetDefinition } from "../lib/ticket-targets.mjs";
import { ModelStoragePicker } from "./model-storage-picker";
import { MailSettings } from "./mail-settings";
import { MailHistory } from "./mail-history";
import { SetupWizard } from "./setup-wizard";

const services: Array<{
  id: Target;
  name: string;
  guide: string;
  fields: Array<{ key: string; label: string; secret?: boolean; hint?: string }>;
  setupSteps: string[];
}> = [
  {
    id: "jira", name: "Jira Cloud", guide: "https://id.atlassian.com/manage-profile/security/api-tokens",
    fields: [{ key: "baseUrl", label: "Jira adresi", hint: "https://sirket.atlassian.net" }, { key: "email", label: "Tokenı oluşturan hesabın e-postası" }, { key: "token", label: "Kullanıcı API tokenı (yönetici API key değil)", secret: true }],
    setupSteps: [
      "Atlassian hesabınızın API token sayfasında Create API token ile yeni bir kullanıcı tokenı oluşturun.",
      "Jira adresini https://sirket.atlassian.net biçiminde, tokenı oluşturan hesabın e-postasını ve tokenı girin.",
      "Kaydet düğmesine basın; ardından Bağlantıyı test et ile hem kimliği hem proje erişimini doğrulayın.",
      "Proje yönetimi bölümünde hedefi Jira Cloud, proje anahtarını (örn. IT) seçin.",
    ],
  },
  {
    id: "trello", name: "Trello", guide: "https://trello.com/app-key",
    fields: [{ key: "key", label: "API key", secret: true }, { key: "token", label: "Token", secret: true }],
    setupSteps: [
      "Trello API key sayfasını açın ve API key değerini kopyalayın.",
      "Aynı sayfadaki Token bağlantısından read, write ve account kapsamlı token üretin; süresiz token önerilir.",
      "Kartın açılacağı listenin 24 karakterlik ID'sini board adresinin sonuna .json ekleyip lists[].id alanından alın.",
      "Anahtarları kaydedip test edin; Proje yönetimi bölümünde hedefi Trello ve liste ID'sini girin.",
    ],
  },
  {
    id: "github", name: "GitHub Issues", guide: "https://github.com/settings/tokens?type=beta",
    fields: [{ key: "token", label: "Fine-grained token (Issues: Read and write)", secret: true }, { key: "baseUrl", label: "GitHub Enterprise API adresi (isteğe bağlı)", hint: "https://ghe.sirket.com/api/v3" }],
    setupSteps: [
      "GitHub Settings > Developer settings > Personal access tokens > Fine-grained tokens bölümünden token oluşturun.",
      "Repository access: ilgili depo. Permissions: Issues Read and write, Metadata Read-only. Kuruluşta SSO varsa tokenı yetkilendirin.",
      "Tokenı girin. GitHub Enterprise kullanıyorsanız API adresini https://ghe.sirket.com/api/v3 biçiminde ekleyin.",
      "Proje yönetimi bölümünde hedefi GitHub Issues, depoyu owner/repo olarak girin; depoda Issues sekmesi açık olmalıdır.",
    ],
  },
  {
    id: "asana", name: "Asana", guide: "https://developers.asana.com/docs/authentication",
    fields: [{ key: "token", label: "Personal access token", secret: true }],
    setupSteps: [
      "Asana Developer Console üzerinden kişisel erişim tokenı (PAT) oluşturun.",
      "Tokenı oluşturan kullanıcının projeye üye ve görev oluşturma yetkisi olduğundan emin olun.",
      "Proje adresindeki /0/<proje-GID>/ bölümünden sayısal proje ID'sini alın.",
      "Tokenı kaydedip test edin; Proje yönetimi bölümünde hedefi Asana ve proje GID'sini girin.",
    ],
  },
  {
    id: "clickup", name: "ClickUp", guide: "https://developer.clickup.com/docs/authentication",
    fields: [{ key: "token", label: "Personal API token", secret: true }],
    setupSteps: [
      "ClickUp'ta Settings > Apps > API Token bölümünden kişisel token üretin.",
      "Tokenı oluşturan kullanıcının workspace ve hedef listede görev oluşturma yetkisi olduğundan emin olun.",
      "Liste adresindeki /li/<liste-ID> bölümünden sayısal liste ID'sini alın.",
      "Tokenı kaydedip test edin; Proje yönetimi bölümünde hedefi ClickUp ve liste ID'sini girin.",
    ],
  },
];

export type SettingsSection = "setup" | "project" | "model" | "integrations" | "users" | "mail" | "security";

export function SettingsPanel({ config, project, onSaved, section, onSectionChange }: {
  config: Config; project: ProjectConfig; onSaved: () => Promise<void>;
  section: SettingsSection; onSectionChange: (section: SettingsSection) => void;
}) {
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [setupLoading, setSetupLoading] = useState(true);
  const [projectForm, setProjectForm] = useState({ name: project.name, target: project.target, destinationId: project.destinationId });
  const [users, setUsers] = useState<UserRecord[]>([]);
  const [mailSenders, setMailSenders] = useState<MailSender[]>([]);
  const [mailDomains, setMailDomains] = useState<MailDomain[]>([]);
  const [mailForm, setMailForm] = useState({ name: "", email: "" });
  const [domainForm, setDomainForm] = useState("");
  const [llm, setLlm] = useState({ endpoint: "http://ollama:11434", model: "qwen3:4b" });
  const [status, setStatus] = useState<{ connected: boolean; installed: boolean; error?: string } | null>(null);
  const [storage, setStorage] = useState<ModelStorage | null>(null);
  const [storageError, setStorageError] = useState("");
  const [provider, setProvider] = useState<Target>("jira");
  const [saved, setSaved] = useState<Record<string, Record<string, string | boolean>>>({});
  const [values, setValues] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState("");
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");

  const refreshSetup = useCallback(async () => {
    setSetupLoading(true);
    try { setSetup(await api.setupStatus()); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Kurulum durumu yüklenemedi."); }
    finally { setSetupLoading(false); }
  }, []);

  useEffect(() => {
    api.settings().then((data) => { setLlm(data.llm); setSaved(data.providers); }).catch((reason) => setError(reason instanceof Error ? reason.message : "Ayarlar yüklenemedi."));
    api.users().then((data) => setUsers(data.users)).catch((reason) => setError(reason instanceof Error ? reason.message : "Kullanıcılar yüklenemedi."));
    api.mailSenders().then((data) => setMailSenders(data.senders)).catch((reason) => setError(reason instanceof Error ? reason.message : "İzinli gönderenler yüklenemedi."));
    api.mailDomains().then((data) => setMailDomains(data.domains)).catch((reason) => setError(reason instanceof Error ? reason.message : "İzinli alan adları yüklenemedi."));
    const setupTimer = window.setTimeout(() => { void refreshSetup(); }, 0);
    api.llmStatus().then(setStatus).catch(() => setStatus({ connected: false, installed: false }));
    if (!config.localMode) return () => window.clearTimeout(setupTimer);
    let active = true;
    async function refresh() {
      try {
        const [next, llmStatus] = await Promise.all([api.modelStorage(), api.llmStatus()]);
        if (active) { setStorage(next); setStatus(llmStatus); setStorageError(""); }
      } catch (reason) {
        if (active) setStorageError(reason instanceof Error ? reason.message : "Model konumları yüklenemedi.");
      }
    }
    void refresh();
    const timer = window.setInterval(refresh, 2000);
    return () => { active = false; window.clearTimeout(setupTimer); window.clearInterval(timer); };
  }, [config.localMode, refreshSetup]);

  useEffect(() => {
    if (storage?.job.state === "done") void api.llmStatus().then(setStatus);
  }, [storage?.job.state]);

  useEffect(() => {
    if (section !== "setup") return;
    const timer = window.setTimeout(() => { void refreshSetup(); }, 0);
    return () => window.clearTimeout(timer);
  }, [section, refreshSetup]);

  const selected = services.find((item) => item.id === provider)!;
  const projectTarget = targetDefinition(projectForm.target);
  const projectDestinationError = projectForm.destinationId ? destinationError(projectForm.target, projectForm.destinationId) : "";
  const downloading = storage?.job.state === "running";

  async function saveLlm() {
    setBusy("llm"); setError("");
    try {
      await api.saveSettings({ type: "llm", ...llm });
      setStatus(await api.llmStatus());
      setNotice("Yerel model ayarları kaydedildi.");
      await onSaved();
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Kaydedilemedi."); }
    finally { setBusy(""); }
  }

  async function pull() {
    setBusy("pull"); setError(""); setNotice("");
    try {
      await api.saveSettings({ type: "llm", ...llm });
      await api.pullModel(llm.model);
      setStorage(await api.modelStorage());
      setNotice("Model indirmesi başladı. Bağlantı kesilirse otomatik yeniden denenecek.");
      await onSaved();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "İndirme başlatılamadı."); }
    finally { setBusy(""); }
  }

  async function cancelPull() {
    try { await api.cancelModelPull(); setStorage(await api.modelStorage()); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "İndirme durdurulamadı."); }
  }

  async function saveProvider() {
    setBusy("save"); setError("");
    try {
      await api.saveSettings({ type: "provider", provider, values });
      setSaved((await api.settings()).providers);
      setValues({});
      setNotice(`${selected.name} bilgileri kaydedildi.`);
      await onSaved();
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Kaydedilemedi."); }
    finally { setBusy(""); }
  }

  async function testProvider() {
    setBusy("test"); setError("");
    try { await api.testProvider(provider); setNotice(`${selected.name} bağlantısı doğrulandı.`); void refreshSetup(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Bağlantı doğrulanamadı."); }
    finally { setBusy(""); }
  }

  async function removeProvider() {
    if (!window.confirm(`${selected.name} bağlantı bilgileri kaldırılsın mı?`)) return;
    setBusy("remove"); setError("");
    try {
      await api.removeProvider(provider);
      setSaved((await api.settings()).providers);
      setValues({});
      setNotice(`${selected.name} bağlantısı kaldırıldı.`);
      await onSaved();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Bağlantı kaldırılamadı."); }
    finally { setBusy(""); }
  }

  async function changePassword() {
    setBusy("password"); setError("");
    try {
      await api.changePassword(currentPassword, newPassword);
      setCurrentPassword(""); setNewPassword("");
      setNotice("Şifreniz değiştirildi.");
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Şifre değiştirilemedi."); }
    finally { setBusy(""); }
  }

  async function saveProject() {
    setBusy("project"); setError("");
    try {
      const destinationId = normalizeDestination(projectForm.target, projectForm.destinationId);
      const invalid = destinationError(projectForm.target, destinationId);
      if (invalid) throw new Error(invalid);
      await api.saveProject({ ...projectForm, destinationId });
      setProjectForm({ ...projectForm, destinationId });
      setNotice(`${projectTarget.label} hedefi kaydedildi.`);
      await onSaved();
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Proje kaydedilemedi."); }
    finally { setBusy(""); }
  }

  async function updateUser(id: string, status: "active" | "disabled") {
    setBusy(id); setError("");
    try { await api.updateUser(id, status); setUsers((await api.users()).users); setNotice(status === "active" ? "Hesap onaylandı." : "Hesap devre dışı bırakıldı."); await onSaved(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Kullanıcı güncellenemedi."); }
    finally { setBusy(""); }
  }

  async function saveMailSender(input: { email: string; name: string; active: boolean }) {
    setBusy("mail"); setError("");
    try {
      await api.saveMailSender(input);
      setMailSenders((await api.mailSenders()).senders);
      setMailForm({ name: "", email: "" });
      setNotice("Gönderen kaydı güncellendi.");
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Gönderen kaydedilemedi."); }
    finally { setBusy(""); }
  }

  async function saveDomain() {
    setBusy("domain"); setError("");
    try {
      const result = await api.saveMailDomain(domainForm);
      setMailDomains(result.domains);
      setDomainForm("");
      setNotice("İzinli alan adı eklendi.");
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Alan adı kaydedilemedi."); }
    finally { setBusy(""); }
  }

  async function removeDomain(domain: string) {
    setBusy(`domain-${domain}`); setError("");
    try {
      const result = await api.removeMailDomain(domain);
      setMailDomains(result.domains);
      setNotice("Alan adı kaldırıldı.");
      void refreshSetup();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Alan adı kaldırılamadı."); }
    finally { setBusy(""); }
  }

  return <div className="settings-page">
    {notice && <div className="flash success"><Check size={16}/>{notice}</div>}
    {error && <div className="flash danger"><AlertCircle size={16}/>{error}</div>}
    <div className="settings-workspace"><nav className="settings-subnav" aria-label="Ayarlar bölümleri">
      {([["setup","Kurulum"],["project","Proje yönetimi"],["model","LLM modeli"],["integrations","Entegrasyonlar"],["mail","Mail sistemi"],["users","Kullanıcı onayı"],["security","Şifre ve erişim"]] as const).map(([key,label])=><button key={key} className={section===key?"selected":""} onClick={()=>{onSectionChange(key);setNotice("");setError("");}}>{label}{key==="users"&&users.some(user=>user.status==="pending")&&<span>{users.filter(user=>user.status==="pending").length}</span>}</button>)}
    </nav><div className="settings-content">
    {section==="setup"&&<SetupWizard status={setup} loading={setupLoading} onRefresh={refreshSetup} onNavigate={onSectionChange}/>}
    {section==="project"&&<section className="panel setup-panel"><div className="section-heading"><Server size={21}/><div><h2>Kurum ticket hedefi</h2><p>Çalışanların açtığı talepler varsayılan olarak seçili hedefte kayıt açar. Otomatik mail ticket&apos;ları da bu hedefe gönderilir.</p></div></div><div className="form-row two"><label className="control"><span>Proje adı</span><input value={projectForm.name} onChange={event=>setProjectForm({...projectForm,name:event.target.value})}/></label><label className="control"><span>Ticket sistemi</span><select value={projectForm.target} onChange={event=>setProjectForm({...projectForm,target:event.target.value as ProjectConfig["target"],destinationId:""})}>{TICKET_TARGETS.map(key=><option key={key} value={key}>{targetDefinition(key).label}</option>)}</select></label></div><label className="control"><span>{projectTarget.destinationLabel}</span><input value={projectForm.destinationId} placeholder={projectTarget.destinationPlaceholder} onChange={event=>setProjectForm({...projectForm,destinationId:projectForm.target==="jira"?event.target.value.toUpperCase():projectForm.target==="trello"?event.target.value.trim().toLowerCase():event.target.value.trim()})}/></label><p className="setting-help">{projectTarget.destinationHelp}</p>{projectDestinationError&&<p className="item-error">{projectDestinationError}</p>}<p className="setting-help">Hedef değişikliği yalnızca yeni kayıtları etkiler; geçmiş kayıtlar özgün hedeflerinde kalır.</p><button className="primary" onClick={saveProject} disabled={Boolean(busy)||!projectForm.destinationId||Boolean(projectDestinationError)}><Save size={15}/>Hedefi kaydet</button></section>}
    {section==="users"&&<section className="panel setup-panel"><div className="section-heading"><ShieldCheck size={21}/><div><h2>Kullanıcı onayı</h2><p>Yeni hesaplar onaylanana kadar sisteme giriş yapamaz.</p></div></div><div className="user-admin-list">{users.map(user=><article key={user.id}><span className="avatar-circle">{user.name.slice(0,1).toLocaleUpperCase("tr")}</span><div><strong>{user.name}</strong><small>{user.email} · {user.role==="admin"?"Yönetici":user.status==="pending"?"Onay bekliyor":user.status==="active"?"Aktif":"Devre dışı"}</small></div>{user.role!=="admin"&&<button className={user.status==="active"?"secondary":"primary"} disabled={Boolean(busy)} onClick={()=>updateUser(user.id,user.status==="active"?"disabled":"active")}>{user.status==="active"?"Devre dışı bırak":"Onayla"}</button>}</article>)}</div></section>}
    {section==="mail"&&<MailSettings onNotice={setNotice} onError={setError} onChanged={() => void refreshSetup()}/>}
    {section==="mail"&&<section className="panel setup-panel">
      <div className="section-heading"><Globe2 size={21}/><div><h2>İzinli alan adları</h2><p>Kurum alan adını eklediğinizde o alan adındaki tüm çalışanlar talep gönderebilir. Tek tek adres eklemek daha dar bir izindir. İzin listesi <code>From</code> adresini denetler; kurum mail sunucusunda SPF, DKIM ve DMARC doğrulamasını açık tutun.</p></div></div>
      <div className="user-admin-list">{mailDomains.length===0?<p className="quiet-state">Henüz izinli alan adı yok. Aşağıdan kurum alan adınızı ekleyin.</p>:mailDomains.map((domain)=><article key={domain.domain}><span className="avatar-circle">@</span><div><strong>@{domain.domain}</strong><small>{domain.fromEnv ? "ALLOWED_DOMAINS · .env başlangıç listesi" : "Yönetim panelinden eklendi"}</small></div>{!domain.fromEnv&&<button className="secondary danger-action" disabled={Boolean(busy)} onClick={()=>void removeDomain(domain.domain)}><Trash2 size={14}/>Kaldır</button>}</article>)}</div>
      <div className="inventory-form"><h4>Yeni alan adı ekle</h4><div className="mail-domain-form"><label className="control"><span>Kurum alan adı</span><input value={domainForm} placeholder="sirket.com" onChange={(event)=>setDomainForm(event.target.value.trim().toLowerCase().replace(/^@/,""))}/></label><button className="primary" disabled={Boolean(busy)||domainForm.length<4} onClick={()=>void saveDomain()}><Save size={15}/>Alan adını ekle</button></div><p className="setting-help">Yalnızca kuruma ait alan adlarını ekleyin. Örneğin <code>gmail.com</code> gibi genel bir alan adı eklemek dışarıdan gelen tüm talepleri kabul eder; önerilmez.</p></div>
    </section>}
    {section==="mail"&&<section className="panel setup-panel">
      <div className="section-heading"><ShieldCheck size={21}/><div><h2>İzinli e-posta gönderenler</h2><p>Alan adı iznine ek olarak tek tek adres tanımlayabilirsiniz. Ad soyad ve adres ticket açıklamasına eklenir. Devre dışı bırakılan adres, alan adı izni olsa bile kabul edilmez.</p></div></div>
      <div className="user-admin-list">{mailSenders.map(sender=><article key={sender.email}><span className="avatar-circle">{(sender.name || sender.email).slice(0,1).toLocaleUpperCase("tr")}</span><div><strong>{sender.name || "Ad soyad girilmedi"}</strong><small>{sender.email} · {sender.active ? "İzinli" : "Devre dışı"}{sender.fromEnv ? " · .env başlangıç listesi" : ""}</small></div><button className="secondary" disabled={Boolean(busy)} onClick={()=>setMailForm({name:sender.name,email:sender.email})}>Düzenle</button><button className={sender.active?"secondary":"primary"} disabled={Boolean(busy)} onClick={()=>void saveMailSender({email:sender.email,name:sender.name||sender.email,active:!sender.active})}>{sender.active?"Kapat":"İzin ver"}</button></article>)}</div>
      <div className="inventory-form"><h4>{mailSenders.some(sender=>sender.email===mailForm.email)?"Göndereni düzenle":"Yeni gönderen ekle"}</h4><div className="form-row two"><label className="control"><span>Ad soyad</span><input value={mailForm.name} onChange={event=>setMailForm({...mailForm,name:event.target.value})}/></label><label className="control"><span>E-posta adresi</span><input type="email" value={mailForm.email} onChange={event=>setMailForm({...mailForm,email:event.target.value})}/></label></div><button className="primary" disabled={Boolean(busy)||mailForm.name.trim().length<2||!mailForm.email.includes("@") } onClick={()=>void saveMailSender({email:mailForm.email,name:mailForm.name,active:true})}><Save size={15}/>İzinli göndereni kaydet</button></div>
    </section>}
    {section==="mail"&&<MailHistory onError={setError}/>}
    {section==="security"&&config.localMode&&<div className="security-note"><ShieldCheck size={17}/>Şirket ağında hesap şifreleri, personel ve cihaz verileri için HTTPS kurun. HTTP bağlantısı ağ içinde şifreli değildir.</div>}

    {section==="model"&&config.localMode && (storage
      ? <ModelStoragePicker storage={storage} onChanged={async () => { setStorage(await api.modelStorage()); setStatus(await api.llmStatus()); }}/>
      : <div className="storage-note"><Server size={17}/>{storageError || "Model konumları yükleniyor…"}</div>)}

    {section==="model"&&config.localMode && <section className="panel setup-panel">
      <div className="section-heading"><Server size={21}/><div><h2>Yerel yapay zekâ</h2><p>Mesajlar şirket ağından çıkmadan analiz edilir.</p></div></div>
      <div className="setup-steps"><span>1. Konumu seç</span><span>2. Modeli indir</span><span>3. Bağlantıyı doğrula</span></div>
      <div className="form-row two">
        <label className="control"><span>Model sunucusu</span><input value={llm.endpoint} onChange={(event) => setLlm({ ...llm, endpoint: event.target.value })}/></label>
        <label className="control"><span>Model</span><select value={llm.model} onChange={(event) => setLlm({ ...llm, model: event.target.value })}><option value="qwen3:4b">Qwen3 4B · önerilen</option><option value="qwen3:1.7b">Qwen3 1.7B · daha hafif</option><option value="qwen3:0.6b">Qwen3 0.6B · hızlı deneme</option><option value="qwen3:8b">Qwen3 8B · daha güçlü</option></select></label>
      </div>
      <p className="setting-help">Hazır kurulumda model sunucusu otomatik başlar. Ayrı bir şirket içi Ollama sunucusu için örn. <code>http://192.168.1.50:11434</code>, Docker Desktop ana makinesi için <code>http://host.docker.internal:11434</code> yazın.</p>
      {!status?.connected && <p className="setting-help">Model sunucusuna ulaşılamıyor. Ana bilgisayarda Docker’ın açık olduğunu kontrol edin.</p>}
      <div className="setting-actions">
        <button className="secondary" onClick={saveLlm} disabled={Boolean(busy)}><Save size={16}/>Ayarı kaydet</button>
        <button className="primary" onClick={pull} disabled={Boolean(busy) || downloading || !status?.connected || !storage}>{busy === "pull" || downloading ? <LoaderCircle className="spin" size={16}/> : <Download size={16}/>} {downloading ? "İndiriliyor" : status?.installed ? "Modeli yeniden indir" : "Modeli indir"}</button>
        {downloading && <button className="secondary" onClick={cancelPull}>İndirmeyi durdur</button>}
        <span className={status?.connected && status.installed ? "connection good" : "connection warn"}>{status?.connected ? status.installed ? "Model hazır" : "Sunucu bağlı, model indirilmeli" : "Sunucuya ulaşılamıyor"}</span>
      </div>
      {downloading && <p className="setting-help">{storage.job.progress >= 99 ? "Dosyalar doğrulanıyor" : storage.job.message} · %{storage.job.progress} · Deneme {storage.job.attempt}/5. Sayfadan ayrılsanız da indirme sürer.</p>}
      {storage?.job.state === "error" && <p className="item-error">{storage.job.message}</p>}
      {storage?.job.state === "done" && <p className="connection good">İndirme tamamlandı.</p>}
      {status?.error && <p className="setting-help">{status.error}</p>}
    </section>}

    {section==="integrations"&&<section className="panel setup-panel">
      <div className="section-heading"><ShieldCheck size={21}/><div><h2>Ticket entegrasyonları</h2><p>Yalnızca seçtiğiniz ticket içeriği bu servislere gönderilir. Anahtarlar şifreli saklanır.</p></div></div>
      <div className="provider-tabs">{services.map((item) => <button key={item.id} className={provider === item.id ? "selected" : ""} onClick={() => { setProvider(item.id); setValues({}); setNotice(""); setError(""); }}>{item.name}{config[item.id] && <Check size={13}/>}</button>)}</div>
      <div className="provider-form">
        <h3>{selected.name}</h3>
        <p>Bilgileri kaydettikten sonra bağlantıyı test edin. Proje yönetiminde aynı sistem seçiliyse test, kimlik doğrulamasıyla birlikte kayıtlı hedefe erişimi de denetler.</p>
        <ol className="provider-setup">{selected.setupSteps.map((step) => <li key={step}>{step}</li>)}</ol>
        <p className="setting-help"><a href={selected.guide} target="_blank" rel="noreferrer">{selected.name} anahtar sayfası</a> · <a href={targetDefinition(selected.id).apiDocsUrl} target="_blank" rel="noreferrer">API dokümanı</a></p>
        {provider === "jira" && <p className="setting-help">Atlassian yönetim panelindeki kurum API key’i Jira ticket oluşturmak için kullanılamaz. Hesabınızın API tokenları sayfasında “Create API token” ile kapsam seçmeden token oluşturun.</p>}
        {selected.fields.map((field) => <label className="control" key={field.key}><span>{field.label}</span><input type={field.secret ? "password" : "text"} value={values[field.key] ?? (field.secret ? "" : String(saved[provider]?.[field.key] ?? ""))} placeholder={field.secret && saved[provider]?.[field.key === "key" ? "hasKey" : "hasToken"] ? "Kaydedildi · değiştirmek için yeni değer girin" : field.hint ?? ""} onChange={(event) => setValues({ ...values, [field.key]: event.target.value })}/></label>)}
        <div className="setting-actions"><button className="primary" onClick={saveProvider} disabled={Boolean(busy)}><Save size={16}/>Kaydet</button><button className="secondary" onClick={testProvider} disabled={Boolean(busy)}>Bağlantıyı test et</button>{config[provider] && <button className="secondary danger-action" onClick={removeProvider} disabled={Boolean(busy)}>Bağlantıyı kaldır</button>}</div>
      </div>
    </section>}

    {section==="security"&&config.localMode && <section className="panel setup-panel">
      <div className="section-heading"><ShieldCheck size={21}/><div><h2>Şifre ve erişim</h2><p>Kendi şifrenizi değiştirebilir ve oturumu kapatabilirsiniz.</p></div></div>
      <div className="form-row two"><label className="control"><span>Mevcut şifre</span><input type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)}/></label><label className="control"><span>Yeni şifre (en az 12 karakter)</span><input type="password" autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)}/></label></div>
      <div className="setting-actions"><button className="secondary" disabled={Boolean(busy) || !currentPassword || newPassword.length < 12} onClick={changePassword}>Şifreyi değiştir</button><button className="secondary" onClick={async () => { await api.logout(); window.location.reload(); }}>Çıkış yap</button></div>
    </section>}
    </div></div>
  </div>;
}
