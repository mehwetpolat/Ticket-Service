import { ArrowRight, CheckCircle2, Circle, ExternalLink, LoaderCircle, RefreshCw, ShieldCheck, TicketCheck } from "lucide-react";
import type { SetupStatus } from "../lib/types";

type Section = "setup" | "project" | "model" | "integrations" | "users" | "mail" | "security";

export function SetupWizard({ status, loading, onRefresh, onNavigate }: {
  status: SetupStatus | null;
  loading: boolean;
  onRefresh: () => Promise<void>;
  onNavigate: (section: Section) => void;
}) {
  if (loading && !status) return <section className="panel setup-panel"><p className="storage-note"><LoaderCircle className="spin" size={17}/>Kurulum durumu yükleniyor…</p></section>;

  const completed = status?.steps.filter((step) => step.complete).length ?? 0;
  const total = status?.steps.length ?? 8;
  const percent = Math.round((completed / Math.max(1, total)) * 100);

  return <div className="setup-wizard">
    <section className={`panel setup-hero ${status?.complete ? "ready" : ""}`}>
      <div className="setup-hero-icon">{status?.complete ? <TicketCheck size={26}/> : <ShieldCheck size={26}/>}</div>
      <div>
        <span className="eyebrow">KURULUM SİHİRBAZI</span>
        <h2>{status?.complete ? "Sistem hazır" : "TickeTask'ı birkaç adımda hazırlayın"}</h2>
        <p>{status?.complete
          ? `Mail kanalı canlı ve arka planda çalışıyor. Bu sayfayı kapatsanız da IT kutusuna gelen uygun e-postalar otomatik olarak ${status.ticketSystem.label} kaydına dönüşür.`
          : "Adımları sırayla tamamlayın. Her adımda ne yapmanız gerektiği ve bilgiyi nereden alacağınız yazıyor. Ayarlar kaydedildikçe otomatik ticket açma arka planda devreye girer."}</p>
        <div className="setup-progress" aria-label={`Kurulum tamamlandı: %${percent}`}><span style={{ width: `${percent}%` }}/></div>
        <small>{completed} / {total} adım tamamlandı · %{percent}</small>
      </div>
      <button className="secondary setup-refresh" onClick={() => void onRefresh()} disabled={loading}>{loading ? <LoaderCircle className="spin" size={15}/> : <RefreshCw size={15}/>}Durumu yenile</button>
    </section>

    {status?.complete && <section className="panel setup-ready">
      <CheckCircle2 size={20}/>
      <div><strong>Otomatik ticket açma etkin</strong><p>Mail kanalı {status.mail.dryRun ? "deneme modunda" : "canlı modda"}; {status.mail.status?.message ?? "poller çalışıyor"}. Son 7 günde {status.counts.published} e-posta {status.ticketSystem.label} hedefine aktarıldı, {status.counts.ignored} e-posta boş/otomatik olduğu için atlandı.</p></div>
    </section>}

    <section className="panel setup-panel">
      <div className="section-heading"><ShieldCheck size={21}/><div><h2>Kurulum adımları</h2><p>Bir adıma gitmek için sağdaki düğmeyi kullanın. Ayarlar bölümleri arasında geçiş yapabilirsiniz.</p></div></div>
      <ol className="setup-step-list">
        {status?.steps.map((step, index) => <li key={step.id} className={`setup-step ${step.complete ? "complete" : ""}`}>
          <span className="setup-step-index">{step.complete ? <CheckCircle2 size={19}/> : index + 1}</span>
          <div className="setup-step-body">
            <strong>{step.title}</strong>
            <p>{step.description}</p>
            <small>{step.detail}</small>
            {step.id === "integration" && status?.ticketSystem && <a href={status.ticketSystem.docsUrl} target="_blank" rel="noreferrer">{status.ticketSystem.label} API bilgisi sayfasını aç <ExternalLink size={12}/></a>}
            {step.id === "destination" && status?.ticketSystem && <small>{status.ticketSystem.destinationHelp}</small>}
            {step.id === "mail" && <small>Microsoft 365: outlook.office365.com · 993 · TLS. Google Workspace: imap.gmail.com · 993 · TLS (uygulama parolası gerekir). Kurum içi Exchange: mail yöneticinizden IMAP sunucu ve portu alın.</small>}
          </div>
          {!step.complete && <button className="secondary setup-step-action" onClick={() => onNavigate(step.section as Section)}>Bu adıma git <ArrowRight size={14}/></button>}
          {step.complete && <span className="setup-step-done">Tamam</span>}
        </li>)}
        {!status && <li className="setup-step"><span className="setup-step-index"><Circle size={19}/></span><div className="setup-step-body"><strong>Durum alınamadı</strong><p>Kurulum durumu yüklenemedi. Yenile düğmesine basın.</p></div></li>}
      </ol>
    </section>

    <section className="panel setup-note">
      <ShieldCheck size={19}/>
      <div>
        <strong>Güvenlik ve arka plan çalışması</strong>
        <p>TickeTask yerel cihazda çalışır; e-posta içeriği ve yerel model çağrıları kurum ağından çıkmaz. Yalnızca onaylanan ticket içeriği seçili ticket sistemine gönderilir. Mail şifreleri AES-GCM ile şifreli saklanır, oturumlar HttpOnly + SameSite çerez kullanır. Uygulamayı internete doğrudan açmayın; şirket ağı dışına açmanız gerekiyorsa HTTPS ters vekil arkasına alın ve <code>APP_PUBLIC_ORIGIN</code> ayarlayın.</p>
        <p>Arayüz kapalıyken de uygulama içindeki arka plan IMAP servisi 15 saniyede bir çalışır; izinli gönderenlerden gelen uygun e-postaları yerel LLM ile analiz edip seçili hedefte ticket açar. Boş, otomatik yanıt ve bülten e-postaları ticket&apos;a dönüşmez.</p>
      </div>
    </section>
  </div>;
}
