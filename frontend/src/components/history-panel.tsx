import { useEffect, useState } from "react";
import { ArrowUpRight, Check, FileText, Inbox, MessageSquareText, RefreshCw, Save, Search, Send, X } from "lucide-react";
import { api } from "../lib/api";
import type { Message, SessionUser, Ticket, TicketComment, TicketWorkflowStatus, UserRecord } from "../lib/types";
import { targetDefinition } from "../lib/ticket-targets.mjs";

const workflowLabels: Record<TicketWorkflowStatus, string> = {
  open: "Açık",
  inprogress: "İşlemde",
  waiting: "Bekliyor",
  resolved: "Çözüldü",
  closed: "Kapatıldı",
};

const workflowOrder: TicketWorkflowStatus[] = ["open", "inprogress", "waiting", "resolved", "closed"];
const ticketTypes = ["Hata", "Talep", "Yeni özellik", "Değişiklik"];
const priorities: Array<{ value: Ticket["priority"]; label: string }> = [
  { value: "critical", label: "Kritik" },
  { value: "high", label: "Yüksek" },
  { value: "normal", label: "Normal" },
];

export function MessagesPanel({ messages, onAnalyze }: { messages: Message[]; onAnalyze: (message: Message) => void }) {
  const [query, setQuery] = useState("");
  const [detail, setDetail] = useState<Message | null>(null);
  const filtered = messages.filter((message) => [message.content, message.personName, message.deviceName, message.createdByName].some((value) => value?.toLocaleLowerCase("tr").includes(query.toLocaleLowerCase("tr"))));
  return <div className="content-width"><div className="page-intro"><div><span className="eyebrow">KAYITLAR</span><h2>Destek mesajları</h2><p>Mesajın tamamını açıp inceleyebilir, gerekirse ticket taslağı hazırlayabilirsiniz.</p></div><span className="summary-pill">{messages.length} mesaj</span></div>
    <label className="history-search"><Search size={17}/><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Kişi, cihaz veya mesaj içinde ara"/></label>
    <div className="history-list">{filtered.length ? filtered.map((message) => <article className="history-row" key={message.id}><span className="history-icon"><Inbox size={18}/></span><button className="history-main" onClick={() => setDetail(message)}><strong>{message.subject || message.content.slice(0, 90)}</strong><span>{message.personName || message.createdByName || message.sender || message.customerName || "Bilinmeyen bildiren"}{message.deviceName ? ` · ${message.deviceName}` : ""} · {date(message.receivedAt)}</span></button><span className="soft-status">{message.status}</span><button className="row-action" onClick={() => onAnalyze(message)}>Analiz et</button></article>) : <Empty text="Bu aramaya uygun mesaj yok."/>}</div>
    {detail && <Detail title={detail.subject || "Destek mesajı"} onClose={() => setDetail(null)}><Info label="Bildiren" value={detail.personName || detail.createdByName || detail.sender || "Belirtilmedi"}/><Info label="Cihaz" value={detail.deviceName || "Belirtilmedi"}/><Info label="Tarih" value={date(detail.receivedAt)}/><h3>Mesajın tamamı</h3><p className="detail-text">{detail.content}</p><button className="primary" onClick={() => { onAnalyze(detail); setDetail(null); }}>Bu mesajı analiz et</button></Detail>}
  </div>;
}

export function TicketsPanel({ tickets, messages, user, onRetry, onChanged, onError }: {
  tickets: Ticket[]; messages: Message[]; user: SessionUser;
  onRetry: (id: string) => Promise<void>; onChanged: () => Promise<void>; onError: (message: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<"all" | "active" | TicketWorkflowStatus>("all");
  const [detail, setDetail] = useState<Ticket | null>(null);
  const [busyId, setBusyId] = useState("");
  const normalized = query.toLocaleLowerCase("tr");
  const filtered = tickets.filter((ticket) => {
    const matchesText = [ticket.title, ticket.description, ticket.personName, ticket.deviceName, ticket.externalId, ticket.createdByName, ticket.assignedToName].some((value) => value?.toLocaleLowerCase("tr").includes(normalized));
    const matchesFilter = filter === "all" || (filter === "active" ? ["open", "inprogress", "waiting"].includes(ticket.workflowStatus) : ticket.workflowStatus === filter);
    return matchesText && matchesFilter;
  });
  async function retry(id: string) { setBusyId(id); try { await onRetry(id); await onChanged(); } finally { setBusyId(""); } }
  const detailTarget = detail ? targetDefinition(detail.target) : null;
  return <div className="content-width"><div className="page-intro"><div><span className="eyebrow">SERVİS MASASI</span><h2>Ticket yönetimi</h2><p>Yerel ticket akışını yönetin, ekip notlarını ekleyin ve hazır olduğunda harici sistemde yayınlayın.</p></div><span className="summary-pill">{tickets.length} ticket</span></div>
    <label className="history-search"><Search size={17}/><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Başlık, kayıt numarası, çalışan, atanan kişi veya cihaz ara"/></label>
    <div className="filter-chips" role="group" aria-label="Ticket durumu">
      {(["all", "active", ...workflowOrder] as const).map((item) => <button key={item} className={filter === item ? "selected" : ""} onClick={() => setFilter(item)}>{item === "all" ? "Tümü" : item === "active" ? "Açık işler" : workflowLabels[item]}</button>)}
    </div>
    <div className="history-list">{filtered.length ? filtered.map((ticket) => {
      const info = targetDefinition(ticket.target);
      return <article className="history-row ticket-row" key={ticket.id}>
        <span className={`workflow-rail ${ticket.workflowStatus}`} aria-hidden="true"/>
        <span className="history-icon"><FileText size={18}/></span>
        <button className="history-main" onClick={() => setDetail(ticket)}>
          <span className={`workflow-badge ${ticket.workflowStatus}`}>{workflowLabels[ticket.workflowStatus]}</span>
          <strong>{ticket.title}</strong>
          <span>{ticket.personName || ticket.createdByName || ticket.customerName || "Kurum içi"}{ticket.deviceName ? ` · ${ticket.deviceName}` : ""} · {date(ticket.createdAt)}{ticket.assignedToName ? ` · Atanan: ${ticket.assignedToName}` : ""}</span>
        </button>
        {ticket.commentCount > 0 && <span className="comment-count"><MessageSquareText size={13}/>{ticket.commentCount}</span>}
        <span className={`history-status ${ticket.status}`}>{ticket.status === "published" ? "Gönderildi" : ticket.status === "failed" ? "Hata" : "Taslak"}</span>
        {ticket.externalUrl && <a className="row-icon-link" href={ticket.externalUrl} target="_blank" rel="noreferrer" aria-label={`${ticket.externalId} kaydını aç`}>{ticket.externalId} <ArrowUpRight size={14}/></a>}
        {ticket.status !== "published" && <button className="row-action" disabled={busyId === ticket.id} onClick={() => retry(ticket.id)}>{busyId === ticket.id ? <RefreshCw className="spin" size={14}/> : <Send size={14}/>}{busyId === ticket.id ? "Gönderiliyor…" : "Gönder"}</button>}
        <small className="history-target">{info.label}</small>
      </article>;
    }) : <Empty text="Bu filtreye uygun ticket yok."/>}</div>
    {detail && detailTarget && <TicketDetail ticket={detail} targetLabel={detailTarget.label} messages={messages} user={user}
      onClose={() => setDetail(null)} onChanged={onChanged} onError={onError}
      onPublish={async () => { await retry(detail.id); setDetail((current) => current ? { ...current, status: "published" } : current); }}/>}
  </div>;
}

function TicketDetail({ ticket, targetLabel, messages, user, onClose, onChanged, onError, onPublish }: {
  ticket: Ticket; targetLabel: string; messages: Message[]; user: SessionUser;
  onClose: () => void; onChanged: () => Promise<void>; onError: (message: string) => void; onPublish: () => Promise<void>;
}) {
  const manager = user.role === "admin" || ticket.createdBy === user.id;
  const assignee = ticket.assignedTo === user.id;
  const canWrite = manager || assignee;
  const [title, setTitle] = useState(ticket.title);
  const [description, setDescription] = useState(ticket.description);
  const [type, setType] = useState(ticket.type);
  const [priority, setPriority] = useState<Ticket["priority"]>(ticket.priority);
  const [dueDate, setDueDate] = useState(ticket.dueDate ?? "");
  const [labels, setLabels] = useState(ticket.labels.join(", "));
  const [workflow, setWorkflow] = useState<TicketWorkflowStatus>(ticket.workflowStatus);
  const [assignedTo, setAssignedTo] = useState(ticket.assignedTo ?? "");
  const [users, setUsers] = useState<UserRecord[]>([]);
  const [comments, setComments] = useState<TicketComment[]>([]);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [commentBusy, setCommentBusy] = useState(false);
  const [publishBusy, setPublishBusy] = useState(false);
  const source = ticket.messageId ? messages.find((message) => message.id === ticket.messageId) : null;

  useEffect(() => {
    let active = true;
    api.ticketComments(ticket.id).then((data) => { if (active) setComments(data.comments); }).catch((reason) => { if (active) onError(reason instanceof Error ? reason.message : "Notlar yüklenemedi."); });
    if (user.role === "admin") api.users().then((data) => { if (active) setUsers(data.users.filter((item) => item.status === "active")); }).catch(() => {});
    return () => { active = false; };
  }, [ticket.id, user.role]);

  async function save() {
    setBusy(true);
    try {
      const payload = manager
        ? { title, description, type, priority, dueDate, labels: labels.split(",").map((label) => label.trim()).filter(Boolean), workflowStatus: workflow, assignedTo }
        : { workflowStatus: workflow };
      await api.updateTicket(ticket.id, payload);
      await onChanged();
      onClose();
    } catch (reason) {
      onError(reason instanceof Error ? reason.message : "Ticket güncellenemedi.");
    } finally { setBusy(false); }
  }

  async function addComment() {
    const body = comment.trim();
    if (!body) return;
    setCommentBusy(true);
    try {
      const created = await api.addTicketComment(ticket.id, body);
      setComments((current) => [...current, created]);
      setComment("");
      await onChanged();
    } catch (reason) {
      onError(reason instanceof Error ? reason.message : "Not eklenemedi.");
    } finally { setCommentBusy(false); }
  }

  async function publish() {
    setPublishBusy(true);
    try { await onPublish(); } finally { setPublishBusy(false); }
  }

  return <div className="modal-backdrop" onClick={onClose}><section className="detail-modal ticket-detail" role="dialog" aria-modal="true" aria-label={ticket.title} onClick={(event) => event.stopPropagation()}>
    <header><div><span className="eyebrow">TICKET · {ticket.id.slice(-8).toUpperCase()}</span><h2>{ticket.title}</h2><div className="ticket-head-badges"><span className={`workflow-badge ${ticket.workflowStatus}`}>{workflowLabels[ticket.workflowStatus]}</span><span className={`history-status ${ticket.status}`}>{ticket.status === "published" ? "Gönderildi" : ticket.status === "failed" ? "Hata" : "Taslak"}</span><span className="soft-status">{targetLabel}{ticket.externalId ? ` · ${ticket.externalId}` : ""}</span></div></div><button aria-label="Kapat" onClick={onClose}><X size={20}/></button></header>
    <div className="detail-body">
      {ticket.error && <p className="item-error">{ticket.error}</p>}
      <section className="ticket-facts">
        <Info label="Bildiren" value={ticket.personName || ticket.createdByName || ticket.customerName || "Belirtilmedi"}/>
        <Info label="Cihaz" value={ticket.deviceName || "Belirtilmedi"}/>
        <Info label="Atanan" value={ticket.assignedToName || "Atanmadı"}/>
        <Info label="Oluşturma" value={date(ticket.createdAt)}/>
      </section>

      {canWrite && <div className="ticket-form">
        <div className="form-row two">
          <label className="control"><span>Durum</span><select value={workflow} onChange={(event) => setWorkflow(event.target.value as TicketWorkflowStatus)}>{workflowOrder.map((item) => <option key={item} value={item}>{workflowLabels[item]}</option>)}</select></label>
          {manager && <label className="control"><span>Atanan kişi</span><select value={assignedTo} onChange={(event) => setAssignedTo(event.target.value)}><option value="">Atanmadı</option>{users.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        </div>
        {manager && <>
          <label className="control"><span>Başlık</span><input value={title} minLength={3} maxLength={120} onChange={(event) => setTitle(event.target.value)}/></label>
          <label className="control"><span>Açıklama</span><textarea rows={7} value={description} onChange={(event) => setDescription(event.target.value)}/></label>
          <div className="form-row two">
            <label className="control"><span>Tür</span><select value={type} onChange={(event) => setType(event.target.value)}>{ticketTypes.map((item) => <option key={item}>{item}</option>)}</select></label>
            <label className="control"><span>Öncelik</span><select value={priority} onChange={(event) => setPriority(event.target.value as Ticket["priority"])}>{priorities.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
          </div>
          <div className="form-row two">
            <label className="control"><span>Son tarih</span><input type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)}/></label>
            <label className="control"><span>Etiketler</span><input value={labels} placeholder="yazici, ag" onChange={(event) => setLabels(event.target.value)}/></label>
          </div>
        </>}
        <div className="form-actions"><button className="primary" disabled={busy} onClick={save}>{busy ? <RefreshCw className="spin" size={15}/> : <Save size={15}/>}{busy ? "Kaydediliyor…" : "Değişiklikleri kaydet"}</button>{ticket.status !== "published" && manager && <button className="secondary" disabled={publishBusy} onClick={publish}><Send size={15}/>{publishBusy ? "Gönderiliyor…" : "Harici sisteme gönder"}</button>}{ticket.externalUrl && <a className="secondary row-icon-link" href={ticket.externalUrl} target="_blank" rel="noreferrer">Kaydı aç <ArrowUpRight size={14}/></a>}</div>
      </div>}

      {!canWrite && <p className="quiet-state">Bu ticket üzerinde görüntüleme yetkiniz var. Düzenleme için ticket sahibine veya yöneticiye başvurun.</p>}

      {manager && ticket.subTasks.length > 0 && <><h3>Alt görevler</h3><ul className="detail-tasks">{ticket.subTasks.map((task, index) => <li key={index}><strong>{task.title}</strong>{task.description && <p>{task.description}</p>}{task.externalId && <small>{task.externalId}</small>}</li>)}</ul></>}
      {source && <><h3>Kaynak mesaj</h3><p className="detail-text">{source.content}</p></>}

      <section className="comments-section">
        <div className="comments-head"><h3>Ekip notları</h3><span>{comments.length}</span></div>
        <div className="comments-list">{comments.length ? comments.map((item) => <article className="comment" key={item.id}><span className="avatar-circle">{(item.authorName || "?").slice(0, 1).toLocaleUpperCase("tr")}</span><div><header><strong>{item.authorName || "Kullanıcı"}</strong><small>{date(item.createdAt)}</small></header><p>{item.body}</p></div></article>) : <p className="quiet-state">Henüz not eklenmedi.</p>}</div>
        {canWrite && <div className="comment-form"><textarea rows={3} value={comment} maxLength={4000} placeholder="Yapılan işi, gözlemi veya müşteri yanıtını yazın…" onChange={(event) => setComment(event.target.value)}/><button className="primary" disabled={commentBusy || !comment.trim()} onClick={addComment}>{commentBusy ? <RefreshCw className="spin" size={15}/> : <Check size={15}/>}{commentBusy ? "Ekleniyor…" : "Not ekle"}</button></div>}
      </section>
    </div>
  </section></div>;
}

function Detail({ title, children, onClose }: { title: string; children: React.ReactNode; onClose: () => void }) {
  return <div className="modal-backdrop" onClick={onClose}><section className="detail-modal" role="dialog" aria-modal="true" aria-label={title} onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">KAYIT DETAYI</span><h2>{title}</h2></div><button aria-label="Kapat" onClick={onClose}><X size={20}/></button></header><div className="detail-body">{children}</div></section></div>;
}
function Info({ label, value }: { label: string; value: string }) { return <div className="detail-info"><small>{label}</small><strong>{value}</strong></div>; }
function Empty({ text }: { text: string }) { return <div className="history-empty">{text}</div>; }
function date(timestamp: number) { return new Date(timestamp * 1000).toLocaleString("tr-TR"); }
