import { useCallback, useEffect, useState } from "react";
import { AlertCircle, ArrowRight, ArrowUpRight, Check, CirclePlus, Cpu, FileText, Inbox, LayoutDashboard, LoaderCircle, LogOut, RefreshCw, Settings, ShieldCheck, TicketCheck, Users } from "lucide-react";
import { api } from "../lib/api";
import type { Config, Customer, Device, Message, Person, ProjectConfig, SessionUser, SetupStatus, Ticket } from "../lib/types";
import { ComposePanel } from "./compose-panel";
import { TicketsPanel, MessagesPanel } from "./history-panel";
import { InventoryPanel } from "./inventory-panel";
import { ExternalTargetsPanel } from "./external-targets-panel";
import { SettingsPanel, type SettingsSection } from "./settings-panel";
import { AccountPanel } from "./account-panel";
import { targetLabel } from "../lib/ticket-targets.mjs";

type View = "overview" | "compose" | "inbox" | "tickets" | "inventory" | "targets" | "settings";
const emptyConfig: Config = { localModel: false, jira: false, trello: false, github: false, asana: false, clickup: false, inboundWebhook: false, whatsapp: false, localMode: false };
const emptyProject: ProjectConfig = { target: "jira", destinationId: "", name: "Kurum içi BT" };

export function TickeTaskApp() {
  const [view, setView] = useState<View>("overview");
  const [settingsSection, setSettingsSection] = useState<SettingsSection>("setup");
  const [user, setUser] = useState<SessionUser | null>(null);
  const [project, setProject] = useState<ProjectConfig>(emptyProject);
  const [people, setPeople] = useState<Person[]>([]);
  const [devices, setDevices] = useState<Device[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [messages, setMessages] = useState<Message[]>([]);
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [config, setConfig] = useState<Config>(emptyConfig);
  const [setup, setSetup] = useState<SetupStatus | null>(null);
  const [source, setSource] = useState<Message | null>(null);
  const [loading, setLoading] = useState(true);
  const [loginRequired, setLoginRequired] = useState(false);
  const [notice, setNotice] = useState("");
  const [noticeUrl, setNoticeUrl] = useState("");
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      const data = await api.dashboard();
      setUser(data.user); setProject(data.project); setPeople(data.people); setDevices(data.devices);
      setCustomers(data.customers); setMessages(data.messages); setTickets(data.tickets); setConfig(data.config);
      setLoginRequired(false);
      if (data.user.role === "admin") {
        try { setSetup(await api.setupStatus()); } catch { setSetup(null); }
      } else {
        setSetup(null);
      }
    } catch (reason) {
      const message = reason instanceof Error ? reason.message : "Veriler yüklenemedi.";
      if (message === "Oturum gerekli.") setLoginRequired(true);
      else setError(message);
    } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  function openCompose(message?: Message) { setSource(message ?? null); setView("compose"); setError(""); }
  function openSettings(section: SettingsSection) { setSettingsSection(section); setView("settings"); setError(""); }
  async function retry(id: string) {
    try {
      const result = await api.retryTicket(id);
      setNotice(`Ticket kaydı hazır: ${result.externalId}.`); setNoticeUrl(result.externalUrl);
      setError(""); await load();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Gönderim tamamlanamadı."); }
  }

  if (loading) return <main className="initial-loading"><LoaderCircle className="spin" size={28}/><p>TickeTask hazırlanıyor…</p></main>;
  if (loginRequired || !user) return <Login onLogin={load}/>;
  const admin = user.role === "admin";
  const pending = tickets.filter((ticket) => ["open", "inprogress", "waiting"].includes(ticket.workflowStatus)).length;
  const unread = messages.filter((message) => message.status === "new").length;
  const labels: Record<View, string> = { overview: "Genel bakış", compose: "Yeni ticket", inbox: "Mesajlar", tickets: "Ticket yönetimi", inventory: "Envanter", targets: "Harici taraflar", settings: admin ? "Yönetim ayarları" : "Hesabım" };

  return <div className="product-shell workspace-shell">
    <aside className="product-nav workspace-nav">
      <div className="product-logo"><span><TicketCheck size={18}/></span><div>TickeTask<small>IT SERVICE DESK</small></div></div>
      <button className="new-task" onClick={() => openCompose()}><CirclePlus size={17}/>Yeni talep oluştur</button>
      <nav aria-label="Ana menü">
        {admin && setup && !setup.complete && <Nav active={view === "settings" && settingsSection === "setup"} icon={<ShieldCheck size={17}/>} label="Kurulum" onClick={() => openSettings("setup")}/>}
        <Nav active={view === "overview"} icon={<LayoutDashboard size={17}/>} label="Genel bakış" onClick={() => setView("overview")}/>
        <Nav active={view === "inbox"} icon={<Inbox size={17}/>} label="Mesajlar" count={unread} onClick={() => setView("inbox")}/>
        <Nav active={view === "tickets"} icon={<FileText size={17}/>} label="Ticket yönetimi" count={pending} onClick={() => setView("tickets")}/>
        <Nav active={view === "inventory"} icon={<Cpu size={17}/>} label={admin ? "Envanter" : "Cihazlarım"} onClick={() => setView("inventory")}/>
        {admin && <><span className="nav-caption">YÖNETİM</span><Nav active={view === "targets"} icon={<Users size={17}/>} label="Harici taraflar" onClick={() => setView("targets")}/></>}
        <Nav active={view === "settings"} icon={admin ? <Settings size={17}/> : <ShieldCheck size={17}/>} label={admin ? "Ayarlar" : "Hesabım"} onClick={() => setView("settings")}/>
      </nav>
      <div className="nav-profile"><span className="avatar-circle">{user.name.slice(0, 1).toLocaleUpperCase("tr")}</span><div><strong>{user.name}</strong><small>{admin ? "Yönetici" : "Çalışan"}</small></div><button aria-label="Çıkış yap" onClick={async () => { await api.logout(); window.location.reload(); }}><LogOut size={16}/></button></div>
    </aside>
    <main className="product-main workspace-main">
      <header className="product-header workspace-header"><div><small>{project.name} / {admin ? "Yönetici alanı" : "Kişisel alan"}</small><h1>{labels[view]}</h1></div><div className="header-actions"><span className="header-project">{project.destinationId ? `${targetLabel(project.target)} · ${project.destinationId}` : "Hedef ayarı gerekli"}</span><button className="refresh" onClick={() => { setError(""); void load(); }}><RefreshCw size={16}/>Yenile</button></div></header>
      {notice && <div className="flash success"><Check size={17}/>{notice}{noticeUrl && <a href={noticeUrl} target="_blank" rel="noreferrer">Kaydı aç <ArrowUpRight size={14}/></a>}</div>}
      {error && <div className="flash danger"><AlertCircle size={17}/>{error}<button onClick={() => setError("")} aria-label="Kapat">×</button></div>}
      {view === "overview" && <Overview user={user} project={project} tickets={tickets} messages={messages} devices={devices} setup={setup} onSetup={() => openSettings("setup")} onCompose={() => openCompose()} onTickets={() => setView("tickets")} onMessages={() => setView("inbox")}/>}
      {view === "compose" && <ComposePanel key={source?.id ?? "new"} user={user} project={project} people={people} devices={devices} customers={customers} config={config} source={source} onError={setError} onDone={(message, url) => { setNotice(message); setNoticeUrl(url ?? ""); setView("tickets"); void load(); }}/>}
      {view === "inbox" && <MessagesPanel messages={messages} onAnalyze={(message) => openCompose(message)}/>}
      {view === "tickets" && <TicketsPanel tickets={tickets} messages={messages} user={user} onRetry={retry} onChanged={load} onError={setError}/>}
      {view === "inventory" && <InventoryPanel user={user} people={people} devices={devices} onChanged={load} onError={setError}/>}
      {view === "targets" && admin && <ExternalTargetsPanel customers={customers} onChanged={load} onError={setError}/>}
      {view === "settings" && (admin ? <SettingsPanel config={config} project={project} section={settingsSection} onSectionChange={setSettingsSection} onSaved={load}/> : <AccountPanel user={user} onError={setError}/>)}
    </main>
  </div>;
}

function Nav({ active, icon, label, count, onClick }: { active: boolean; icon: React.ReactNode; label: string; count?: number; onClick: () => void }) {
  return <button className={active ? "active" : ""} onClick={onClick}>{icon}<span>{label}</span>{Boolean(count) && <b>{count}</b>}</button>;
}

function Overview({ user, project, tickets, messages, devices, setup, onSetup, onCompose, onTickets, onMessages }: {
  user: SessionUser; project: ProjectConfig; tickets: Ticket[]; messages: Message[]; devices: Device[];
  setup: SetupStatus | null; onSetup: () => void; onCompose: () => void; onTickets: () => void; onMessages: () => void;
}) {
  const active = tickets.filter((ticket) => ["open", "inprogress", "waiting"].includes(ticket.workflowStatus)).length;
  const resolved = tickets.filter((ticket) => ticket.workflowStatus === "resolved" || ticket.workflowStatus === "closed").length;
  const drafts = tickets.filter((ticket) => ticket.status !== "published").length;
  return <div className="content-width overview-page">
    <section className="welcome-banner"><div><span className="eyebrow">KURUM İÇİ BT DESTEK</span><h2>Merhaba, {user.name.split(" ")[0]}</h2><p>Gelen mesajları yerel modelle analiz edin; kişi ve cihaz bağlamını doğrulayın, ticket akışını burada yönetin, hazır olduğunda seçili harici sisteme gönderin.</p><button onClick={onCompose}>Yeni ticket hazırla <ArrowRight size={17}/></button></div><div className="welcome-symbol"><TicketCheck size={70}/></div></section>
    {setup && !setup.complete && <SetupCard status={setup} onOpen={onSetup}/>}
    <div className="stat-grid"><button onClick={onTickets}><span>Açık işler</span><strong>{active}</strong><small>Açık, işlemde ve bekleyen <ArrowRight size={13}/></small></button><button onClick={onTickets}><span>Çözülen</span><strong>{resolved}</strong><small>Çözüldü ve kapatıldı <ArrowRight size={13}/></small></button><button onClick={onTickets}><span>Yayın bekleyen</span><strong>{drafts}</strong><small>Taslak veya hatada <ArrowRight size={13}/></small></button><button onClick={onMessages}><span>Yeni mesaj</span><strong>{messages.filter((message) => message.status === "new").length}</strong><small>Mesajları aç <ArrowRight size={13}/></small></button></div>
    <div className="overview-grid"><section className="panel overview-card"><div className="panel-top"><h3>Son ticket’lar</h3><button onClick={onTickets}>Tümünü gör <ArrowRight size={15}/></button></div>{tickets.length ? tickets.slice(0, 5).map((ticket) => <div className="mini-row" key={ticket.id}><span className={`workflow-dot ${ticket.workflowStatus}`}/><div><strong>{ticket.title}</strong><small>{ticket.personName || ticket.createdByName || "Kurum içi"}{ticket.deviceName ? ` · ${ticket.deviceName}` : ""}{ticket.assignedToName ? ` · ${ticket.assignedToName}` : ""}</small></div><em className={`workflow-badge ${ticket.workflowStatus}`}>{workflowLabel(ticket.workflowStatus)}</em></div>) : <p className="quiet-state">Henüz ticket yok. İlk talebi oluşturun.</p>}</section><section className="panel overview-card"><div className="panel-top"><h3>Çalışma alanı</h3></div><div className="quick-info"><span>Harici hedef</span><strong>{targetLabel(project.target)} · {project.destinationId || "Ayarlanacak"}</strong></div><div className="quick-info"><span>Cihaz envanteri</span><strong>{devices.length} cihaz</strong></div><div className="quick-info"><span>Rolünüz</span><strong>{user.role === "admin" ? "Yönetici" : "Çalışan"}</strong></div><p className="setting-help">Ticket durumu, atama ve ekip notları TickeTask içinde tutulur. Harici sistem yalnızca siz gönderdiğinizde kullanılır.</p></section></div>
  </div>;
}

function SetupCard({ status, onOpen }: { status: SetupStatus; onOpen: () => void }) {
  const total = Math.max(1, status.steps.length);
  const completed = status.steps.filter((step) => step.complete).length;
  const percent = Math.round((completed / total) * 100);
  const next = status.steps.find((step) => !step.complete);
  const fresh = completed === 0;
  return <section className={`panel setup-invite ${fresh ? "fresh" : ""}`}>
    <span className="setup-invite-icon"><ShieldCheck size={25}/></span>
    <div className="setup-invite-body">
      <span className="eyebrow">{fresh ? "İLK KURULUM GEREKLİ" : "KURULUM DEVAM EDİYOR"}</span>
      <h2>{fresh ? "TickeTask'ı kurmaya başlayın" : "Kurulumu tamamlayın"}</h2>
      <p>{fresh
        ? "Henüz hiçbir ayar yapılmadı. Mail kutusu, ticket sistemi ve yerel model adımlarını kurulum ekranından tamamlayın; ardından otomatik ticket açma çalışmaya başlar."
        : next ? `Sıradaki adım: ${next.title}. ${next.detail}` : "Kurulum adımlarını tamamlayarak otomatik ticket açmayı etkinleştirin."}</p>
      <div className="setup-progress" aria-label={`Kurulum tamamlandı: %${percent}`}><span style={{ width: `${percent}%` }}/></div>
      <small>{completed} / {total} adım tamamlandı · %{percent}</small>
    </div>
    <button className="primary" onClick={onOpen}>{fresh ? "Kurulumu başlat" : "Kurulum ekranını aç"} <ArrowRight size={16}/></button>
  </section>;
}

function workflowLabel(status: Ticket["workflowStatus"]) {
  return status === "open" ? "Açık" : status === "inprogress" ? "İşlemde" : status === "waiting" ? "Bekliyor" : status === "resolved" ? "Çözüldü" : "Kapatıldı";
}

function Login({ onLogin }: { onLogin: () => Promise<void> }) {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  async function submit(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError(""); setNotice("");
    try {
      if (mode === "register") {
        const result = await api.register(name, email, password);
        setNotice(result.message); setMode("login"); setPassword("");
      } else { await api.login(email, password); await onLogin(); }
    } catch (reason) { setError(reason instanceof Error ? reason.message : "İşlem tamamlanamadı."); }
    finally { setBusy(false); }
  }
  return <main className="login-page login-workspace"><div className="login-brand"><span><TicketCheck size={26}/></span><strong>TickeTask</strong><small>Kurum içi BT destek yönetimi</small></div><form className="panel login-card" onSubmit={submit}><span className="eyebrow">GÜVENLİ ERİŞİM</span><h1>{mode === "login" ? "Hesabınıza giriş yapın" : "Hesap oluşturun"}</h1><p>{mode === "login" ? "Kurumsal hesabınızla giriş yapın. İlk kurulumda yönetici, kurulum şifresini kullanır." : "Hesabınız yönetici onayından sonra açılır."}</p>{mode === "register" && <label className="control"><span>Ad soyad</span><input value={name} onChange={(event) => setName(event.target.value)} required/></label>}<label className="control"><span>Kurumsal e-posta</span><input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} required/></label><label className="control"><span>Şifre</span><input type="password" autoComplete={mode === "login" ? "current-password" : "new-password"} minLength={mode === "register" ? 12 : 1} value={password} onChange={(event) => setPassword(event.target.value)} required/></label>{error && <p className="item-error">{error}</p>}{notice && <p className="connection good">{notice}</p>}<button className="primary" disabled={busy}>{busy ? <LoaderCircle size={16} className="spin"/> : null}{mode === "login" ? "Giriş yap" : "Onaya gönder"}</button><button className="login-switch" type="button" onClick={() => { setMode(mode === "login" ? "register" : "login"); setError(""); setNotice(""); }}>{mode === "login" ? "Hesabınız yok mu? Kayıt olun" : "Zaten hesabınız var mı? Giriş yapın"}</button></form></main>;
}
