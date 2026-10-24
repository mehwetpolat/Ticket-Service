import { useState } from "react";
import { AlertCircle, ArrowRight, Check, FileUp, Plus, Send, Sparkles, Trash2 } from "lucide-react";
import { api } from "../lib/api";
import type { Analysis, Config, Customer, Device, Message, Person, ProjectConfig, SessionUser } from "../lib/types";
import type { TicketDraft } from "../lib/ticket-types";
import { targetDefinition } from "../lib/ticket-targets.mjs";

type Props = {
  user: SessionUser; project: ProjectConfig; people: Person[]; devices: Device[]; customers: Customer[];
  config: Config; source: Message | null; onDone: (message: string, url?: string) => void; onError: (message: string) => void;
};

export function ComposePanel({ user, project, people, devices, customers, config, source, onDone, onError }: Props) {
  const own = people.find((person) => person.userId === user.id);
  const [personId, setPersonId] = useState(source?.personId ?? own?.id ?? "");
  const [deviceId, setDeviceId] = useState(source?.deviceId ?? "");
  const [customerId, setCustomerId] = useState(source?.customerId ?? "");
  const [message, setMessage] = useState(source?.content ?? "");
  const [messageId, setMessageId] = useState<string | null>(source?.id ?? null);
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [attachments, setAttachments] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const personDevices = devices.filter((device) => device.personId === personId);
  const selectedCustomer = customers.find((customer) => customer.id === customerId);
  const target = selectedCustomer?.target ?? project.target;
  const destination = selectedCustomer?.destinationId ?? project.destinationId;
  const targetInfo = targetDefinition(target);
  const connected = Boolean(config[target]);
  const canPublish = Boolean(destination && connected);
  const publishHint = !connected ? `${targetInfo.shortLabel} bağlantısı gerekli` : !destination ? `${targetInfo.destinationLabel} gerekli` : targetInfo.publishLabel;

  function changeMessage(value: string) { setMessage(value); setMessageId(null); setAnalysis(null); }

  async function analyze() {
    if (message.trim().length < 5) { onError("Sorunu en az 5 karakterle açıklayın."); return; }
    setBusy(true); onError("");
    try {
      let currentId = messageId;
      if (!currentId) {
        currentId = (await api.saveMessage({ customerId: customerId || null, personId: personId || null, deviceId: deviceId || null, channel: "manual", content: message })).id;
        setMessageId(currentId);
      }
      setAnalysis(await api.analyze(message));
    } catch (error) { onError(error instanceof Error ? error.message : "Analiz tamamlanamadı."); }
    finally { setBusy(false); }
  }

  async function submit(publish: boolean) {
    if (!analysis) return;
    setBusy(true); onError("");
    try {
      if (attachments.length > 3 || attachments.some((file) => file.size > 5 * 1024 * 1024) || attachments.reduce((sum, file) => sum + file.size, 0) > 10 * 1024 * 1024) {
        throw new Error("En fazla 3 ek seçin. Tek dosya 5 MB, toplam 10 MB olabilir.");
      }
      const files = await Promise.all(attachments.map(async (file) => ({
        name: file.name, contentType: file.type || "application/octet-stream", data: await fileBase64(file),
      })));
      const result = await api.createTicket({
        customerId: customerId || null, personId: personId || null, deviceId: deviceId || null, messageId,
        analysisMode: analysis.mode, publish, draft: analysis.ticket, attachments: files,
      });
      onDone(result.status === "published" ? `${targetInfo.shortLabel} kaydı oluşturuldu: ${result.externalId}.` : "Taslak kaydedildi.", result.externalUrl);
      setMessage(""); setMessageId(null); setAnalysis(null); setAttachments([]);
    } catch (error) { onError(error instanceof Error ? error.message : "Ticket kaydedilemedi."); }
    finally { setBusy(false); }
  }

  return <div className="work-layout">
    <section className="panel work-panel">
      <div className="work-panel-heading"><span className="step-badge">1</span><div><h2>Destek talebi</h2><p>Kim etkilendi ve hangi cihazda sorun yaşanıyor?</p></div></div>
      <div className="work-select-grid">
        <label className="control"><span>Etkilenen kişi</span>
          {user.role === "admin"
            ? <select value={personId} onChange={(event) => { setPersonId(event.target.value); setDeviceId(""); setMessageId(null); }}><option value="">Kişi seçin (isteğe bağlı)</option>{people.map((person) => <option key={person.id} value={person.id}>{person.name}{person.department ? ` · ${person.department}` : ""}</option>)}</select>
            : <div className="selection-value">{own?.name ?? user.name}</div>}
        </label>
        <label className="control"><span>Cihaz</span><select value={deviceId} onChange={(event) => { setDeviceId(event.target.value); setMessageId(null); }}><option value="">Cihaz seçin (isteğe bağlı)</option>{personDevices.map((device) => <option key={device.id} value={device.id}>{device.name}{device.ipAddress ? ` · ${device.ipAddress}` : ""}</option>)}</select></label>
      </div>
      {user.role === "admin" && customers.length > 0 && <label className="control"><span>Hedef proje</span><select value={customerId} onChange={(event) => { setCustomerId(event.target.value); setMessageId(null); setAttachments([]); }}><option value="">{project.name} · {targetDefinition(project.target).label} {project.destinationId || "ayarlanmadı"}</option>{customers.map((customer) => <option key={customer.id} value={customer.id}>{customer.name} · {targetDefinition(customer.target).label} {customer.destinationId}</option>)}</select></label>}
      <div className="destination-chip"><Check size={15}/>{targetInfo.label} · {destination || `${targetInfo.destinationLabel} ayarlanmadı`} <span>{target === "jira" ? "İş önce backlog'a düşer" : "Kayıt doğrudan hedefte açılır"}</span></div>
      <label className="control"><span>Sorunu anlatın</span><textarea rows={9} value={message} onChange={(event) => changeMessage(event.target.value)} placeholder="Örn. Ayşe'nin LT-042 adlı bilgisayarında PowerPoint lisans uyarısı çıkıyor. Sunumlar açılamıyor..."/></label>
      {target === "jira" && <label className="upload-control"><FileUp size={19}/><span><strong>Dosya ekle</strong><small>Ekran görüntüsü veya belge · 3 dosya / 10 MB</small></span><input type="file" multiple onChange={(event) => setAttachments(Array.from(event.target.files ?? []))}/></label>}
      {attachments.length > 0 && <div className="selected-files">{attachments.map((file) => <span key={file.name}>{file.name}</span>)}</div>}
      <button className="primary work-action" disabled={busy || message.trim().length < 5} onClick={analyze}><Sparkles size={17}/>{busy ? "Hazırlanıyor…" : analysis ? "Yeniden analiz et" : "Mesajı analiz et"}<ArrowRight size={16}/></button>
    </section>
    <section className="panel work-panel">
      <div className="work-panel-heading"><span className="step-badge">2</span><div><h2>Ticket taslağı</h2><p>Göndermeden önce başlık, açıklama ve işleri kontrol edin.</p></div></div>
      {analysis ? <TicketEditor value={analysis.ticket} onChange={(ticket) => setAnalysis({ ...analysis, ticket })} onSave={submit} busy={busy} canPublish={canPublish} publishLabel={publishHint}/>
        : <div className="work-empty"><Sparkles size={26}/><h3>Analiz için hazır</h3><p>Mesajı yazıp analiz ettiğinizde taslak burada açılır. Alt görevleri ve ekleri göndermeden önce düzenleyebilirsiniz.</p></div>}
    </section>
  </div>;
}

function TicketEditor({ value, onChange, onSave, busy, canPublish, publishLabel }: {
  value: TicketDraft; onChange: (ticket: TicketDraft) => void; onSave: (publish: boolean) => void; busy: boolean; canPublish: boolean; publishLabel: string;
}) {
  const set = <K extends keyof TicketDraft>(key: K, next: TicketDraft[K]) => onChange({ ...value, [key]: next });
  const tasks = value.subTasks ?? [];
  const editTask = (index: number, field: "title" | "description", text: string) => set("subTasks", tasks.map((task, position) => position === index ? { ...task, [field]: text } : task));
  return <div className="editor">
    <div className="editor-head"><div><span className="ai-mark"><Sparkles size={15}/>Analiz sonucu</span><h2>Gönderime hazırla</h2></div><span className={`priority ${value.priority}`}>{value.priority === "critical" ? "Kritik" : value.priority === "high" ? "Yüksek" : "Normal"}</span></div>
    <label className="control"><span>Başlık</span><input value={value.title} maxLength={120} onChange={(event) => set("title", event.target.value)}/></label>
    <label className="control"><span>Açıklama</span><textarea rows={6} value={value.description} onChange={(event) => set("description", event.target.value)}/></label>
    <div className="form-row"><label className="control"><span>Tür</span><select value={value.type} onChange={(event) => set("type", event.target.value)}><option>Talep</option><option>Hata</option><option>Yeni özellik</option><option>Değişiklik</option></select></label><label className="control"><span>Öncelik</span><select value={value.priority} onChange={(event) => set("priority", event.target.value as TicketDraft["priority"])}><option value="normal">Normal</option><option value="high">Yüksek</option><option value="critical">Kritik</option></select></label><label className="control"><span>Son tarih</span><input type="date" value={value.dueDate} onChange={(event) => set("dueDate", event.target.value)}/></label></div>
    <label className="control"><span>Etiketler</span><input value={value.labels.join(", ")} onChange={(event) => set("labels", event.target.value.split(",").map((item) => item.trim()).filter(Boolean))}/></label>
    {value.questions.map((question) => <div className="question" key={question}><AlertCircle size={16}/>{question}</div>)}
    <div className="subtask-editor"><div className="subtask-editor-head"><strong>Alt görevler <span className="muted-count">{tasks.length}</span></strong><button type="button" className="secondary" disabled={tasks.length >= 8 || busy} onClick={() => set("subTasks", [...tasks, { title: "", description: "" }])}><Plus size={14}/> Ekle</button></div>
      {tasks.length === 0 ? <p className="setting-help">Bağımsız yapılacak işler varsa buraya ekleyin; tek sorun için boş bırakın.</p> : tasks.map((task, index) => <div className="subtask-row" key={index}><label className="control"><span>{index + 1}. İş</span><input value={task.title} maxLength={120} onChange={(event) => editTask(index, "title", event.target.value)}/></label><label className="control"><span>Not</span><textarea rows={2} value={task.description} maxLength={1000} onChange={(event) => editTask(index, "description", event.target.value)}/></label><button className="secondary" type="button" onClick={() => set("subTasks", tasks.filter((_, position) => position !== index))}><Trash2 size={13}/> Kaldır</button></div>)}
    </div>
    <div className="editor-actions"><button className="secondary" disabled={busy} onClick={() => onSave(false)}>Taslak kaydet</button><button className="primary" disabled={busy || !canPublish} onClick={() => onSave(true)}><Send size={16}/>{publishLabel}</button></div>
  </div>;
}

function fileBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1] ?? "");
    reader.onerror = () => reject(new Error(`${file.name} okunamadı.`));
    reader.readAsDataURL(file);
  });
}
