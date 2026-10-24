import { useState } from "react";
import { Pencil, Plus, Save, Trash2 } from "lucide-react";
import { api } from "../lib/api";
import type { Customer, Target } from "../lib/types";
import { destinationError, TICKET_TARGETS, normalizeDestination, targetDefinition } from "../lib/ticket-targets.mjs";

const empty = { name: "", email: "", phone: "", target: "jira" as Target, destinationId: "" };

export function ExternalTargetsPanel({ customers, onChanged, onError }: {
  customers: Customer[]; onChanged: () => Promise<void>; onError: (message: string) => void;
}) {
  const [form, setForm] = useState<Partial<Customer> & typeof empty>({ ...empty });
  const [busy, setBusy] = useState(false);
  const selected = targetDefinition(form.target);
  const invalidDestination = form.destinationId ? destinationError(form.target, form.destinationId) : "";
  async function save() {
    setBusy(true);
    try {
      const destinationId = normalizeDestination(form.target, form.destinationId);
      const invalid = destinationError(form.target, destinationId);
      if (invalid) throw new Error(invalid);
      await api.saveCustomer({ ...form, destinationId });
      setForm({ ...empty });
      await onChanged();
      onError("");
    }
    catch (error) { onError(error instanceof Error ? error.message : "Kayıt kaydedilemedi."); }
    finally { setBusy(false); }
  }
  async function remove(id: string) {
    if (!window.confirm("Bu kayıt silinsin mi? Bağlı mesaj veya ticket varsa silinmez.")) return;
    try { await api.deleteCustomer(id); await onChanged(); }
    catch (error) { onError(error instanceof Error ? error.message : "Kayıt silinemedi."); }
  }
  return <div className="content-width"><div className="page-intro"><div><span className="eyebrow">EK YÖNLENDİRME</span><h2>Harici taraflar</h2><p>Varsayılan ticket hedefi Proje yönetimi bölümünde seçilir. Bu liste yalnızca farklı bir sisteme veya farklı bir proje/listeye yönlenecek özel kayıtlar içindir.</p></div></div>
    <div className="inventory-grid"><section className="panel inventory-card"><div className="panel-top"><Plus size={18}/><h3>{form.id ? "Kaydı düzenle" : "Yeni taraf ekle"}</h3></div><label className="control"><span>Ad</span><input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })}/></label><div className="form-row two"><label className="control"><span>E-posta</span><input type="email" value={form.email ?? ""} onChange={(event) => setForm({ ...form, email: event.target.value })}/></label><label className="control"><span>Telefon</span><input value={form.phone ?? ""} onChange={(event) => setForm({ ...form, phone: event.target.value })}/></label></div><div className="form-row two"><label className="control"><span>Hedef sistem</span><select value={form.target} onChange={(event) => setForm({ ...form, target: event.target.value as Target, destinationId: "" })}>{TICKET_TARGETS.map((key) => <option key={key} value={key}>{targetDefinition(key).label}</option>)}</select></label><label className="control"><span>{selected.destinationLabel}</span><input value={form.destinationId} placeholder={selected.destinationPlaceholder} onChange={(event) => setForm({ ...form, destinationId: event.target.value })}/></label></div><p className="setting-help">{selected.destinationHelp}</p>{invalidDestination&&<p className="item-error">{invalidDestination}</p>}<div className="form-actions"><button className="primary" onClick={save} disabled={busy || !form.name.trim() || !form.destinationId.trim() || Boolean(invalidDestination)}><Save size={15}/>Kaydet</button>{form.id && <button className="secondary" onClick={() => setForm({ ...empty })}>Vazgeç</button>}</div></section>
      <section className="panel inventory-card"><div className="panel-top"><h3>Kayıtlı taraflar</h3></div><div className="inventory-list">{customers.length ? customers.map((customer) => <article className="inventory-row" key={customer.id}><span className="avatar-circle">{customer.name.slice(0, 1)}</span><span><strong>{customer.name}</strong><small>{targetDefinition(customer.target).label} · {customer.destinationId}</small></span><button aria-label="Düzenle" onClick={() => setForm({ ...customer, email: customer.email ?? "", phone: customer.phone ?? "" })}><Pencil size={15}/></button><button aria-label="Sil" onClick={() => remove(customer.id)}><Trash2 size={15}/></button></article>) : <p className="quiet-state">Özel hedef eklenmemiş.</p>}</div></section></div>
  </div>;
}
