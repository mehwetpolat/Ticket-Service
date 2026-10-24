import { useCallback, useEffect, useState } from "react";
import { AlertCircle, CheckCircle2, Clock3, ExternalLink, LoaderCircle, MailWarning, RefreshCw } from "lucide-react";
import { api } from "../lib/api";
import type { MailIngestion } from "../lib/types";
import { targetShortLabel } from "../lib/ticket-targets.mjs";

const statusLabels: Record<MailIngestion["status"], string> = {
  pending: "Bekliyor",
  processing: "İşleniyor",
  failed: "Hata",
  published: "Ticket açıldı",
  dry_run: "Deneme",
  ignored: "Atlandı",
};

export function MailHistory({ onError }: { onError: (value: string) => void }) {
  const [items, setItems] = useState<MailIngestion[]>([]);
  const [stats, setStats] = useState<Record<string, number>>({});
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    try {
      const result = await api.mailHistory(40);
      setItems(result.items);
      setStats(result.stats);
    } catch (error) { onError(error instanceof Error ? error.message : "Mail işlem geçmişi yüklenemedi."); }
    finally { setLoading(false); }
  }, [onError]);

  useEffect(() => {
    let active = true;
    void api.mailHistory(40).then((result) => {
      if (!active) return;
      setItems(result.items); setStats(result.stats);
    }).catch((error) => { if (active) onError(error instanceof Error ? error.message : "Mail işlem geçmişi yüklenemedi."); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [onError]);

  return <section className="panel setup-panel">
    <div className="section-heading"><MailWarning size={21}/><div><h2>Mail işlem geçmişi</h2><p>Son 7 günün özeti ve son 40 e-posta. Boş, otomatik yanıt veya bülten olan mesajlar <strong>Atlandı</strong> olarak görünür; bunlar için ticket açılmaz.</p></div></div>
    <div className="mail-stats">
      <span><b>{stats.published ?? 0}</b><small>Ticket açıldı</small></span>
      <span><b>{stats.dry_run ?? 0}</b><small>Deneme</small></span>
      <span><b>{stats.failed ?? 0}</b><small>Hata · otomatik tekrar dener</small></span>
      <span><b>{stats.ignored ?? 0}</b><small>Atlandı</small></span>
    </div>
    <div className="setting-actions"><button className="secondary" onClick={() => void load()} disabled={loading}><RefreshCw size={15}/>{loading ? "Yükleniyor…" : "Yenile"}</button><span className="setting-help">Poller hatalı kayıtları üstel aralıklarla otomatik yeniden dener.</span></div>
    {loading ? <p className="storage-note"><LoaderCircle className="spin" size={17}/>Geçmiş yükleniyor…</p> : items.length === 0 ? <p className="quiet-state">Henüz işlenmiş e-posta yok. IT kutusuna bir test e-postası gönderin.</p> : <div className="mail-history-list">
      {items.map((item) => <article key={item.id} className="mail-history-row">
        <span className={`status-pill ${item.status}`}>{item.status === "published" ? <CheckCircle2 size={13}/> : item.status === "failed" ? <AlertCircle size={13}/> : item.status === "dry_run" ? <Clock3 size={13}/> : <MailWarning size={13}/>}{statusLabels[item.status] ?? item.status}</span>
        <div className="mail-history-main">
          <strong>{item.subject || "(Konu yok)"}</strong>
          <small>{item.senderName ? `${item.senderName} · ` : ""}{item.senderEmail} · {item.mailbox}</small>
          {item.error && <p className="item-error">{item.error}</p>}
        </div>
        <div className="mail-history-meta">
          <small>{new Date((item.updatedAt || item.createdAt) * 1000).toLocaleString("tr-TR")}</small>
          {item.externalUrl && <a href={item.externalUrl} target="_blank" rel="noreferrer">{item.target ? targetShortLabel(item.target) : "Kayıt"} <ExternalLink size={12}/></a>}
          {item.attempts > 0 && <small>Deneme: {item.attempts}</small>}
        </div>
      </article>)}
    </div>}
  </section>;
}
