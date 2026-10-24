import { useState } from "react";
import { Cpu, Laptop, Network, Save, UserRound } from "lucide-react";
import { api } from "../lib/api";
import type { Device, Person, SessionUser } from "../lib/types";

const kindNames: Record<Device["kind"], string> = {
  desktop: "Masaüstü PC", laptop: "Dizüstü", tablet: "Tablet", printer: "Yazıcı",
  network: "Ağ cihazı", server: "Sunucu", other: "Diğer",
};
const emptyPerson = { name: "", email: "", department: "", phone: "", userId: null as string | null };
const emptyDevice = { name: "", kind: "laptop" as Device["kind"], ipAddress: "", serial: "", location: "", personId: null as string | null, parentId: null as string | null, notes: "" };

export function InventoryPanel({ user, people, devices, onChanged, onError }: {
  user: SessionUser; people: Person[]; devices: Device[]; onChanged: () => Promise<void>; onError: (message: string) => void;
}) {
  const [personForm, setPersonForm] = useState<Partial<Person> & { name: string }>({ ...emptyPerson });
  const [deviceForm, setDeviceForm] = useState<Partial<Device> & { name: string; kind: Device["kind"] }>({ ...emptyDevice });
  const [busy, setBusy] = useState(false);

  async function savePerson() {
    setBusy(true);
    try { await api.savePerson(personForm); setPersonForm({ ...emptyPerson }); await onChanged(); onError(""); }
    catch (error) { onError(error instanceof Error ? error.message : "Çalışan kaydedilemedi."); }
    finally { setBusy(false); }
  }
  async function saveDevice() {
    setBusy(true);
    try { await api.saveDevice(deviceForm); setDeviceForm({ ...emptyDevice }); await onChanged(); onError(""); }
    catch (error) { onError(error instanceof Error ? error.message : "Cihaz kaydedilemedi."); }
    finally { setBusy(false); }
  }

  return <div className="content-width">
    <div className="page-intro"><div><span className="eyebrow">KURUM ENVANTERİ</span><h2>Çalışanlar ve cihazlar</h2><p>Talepte hangi cihazla ilgilenileceğini netleştirmek için envanteri güncel tutun.</p></div><span className="summary-pill">{people.length} kişi · {devices.length} cihaz</span></div>
    <div className="inventory-grid">
      <section className="panel inventory-card"><div className="panel-top"><UserRound size={18}/><h3>Çalışanlar</h3></div>
        <div className="inventory-list">{people.length ? people.map((person) => <button key={person.id} className="inventory-row" onClick={() => user.role === "admin" && setPersonForm({ ...person })}><span className="avatar-circle">{person.name.slice(0, 1).toLocaleUpperCase("tr")}</span><span><strong>{person.name}</strong><small>{person.department || person.email || "Departman belirtilmedi"}</small></span><em>{devices.filter((device) => device.personId === person.id).length} cihaz</em></button>) : <p className="quiet-state">Henüz çalışan kaydı yok.</p>}</div>
        {user.role === "admin" && <div className="inventory-form"><h4>{personForm.id ? "Çalışanı düzenle" : "Çalışan ekle"}</h4><div className="form-row two"><label className="control"><span>Ad soyad</span><input value={personForm.name} onChange={(event) => setPersonForm({ ...personForm, name: event.target.value })}/></label><label className="control"><span>Departman</span><input value={personForm.department ?? ""} onChange={(event) => setPersonForm({ ...personForm, department: event.target.value })}/></label></div><div className="form-row two"><label className="control"><span>E-posta</span><input type="email" value={personForm.email ?? ""} onChange={(event) => setPersonForm({ ...personForm, email: event.target.value })}/></label><label className="control"><span>Telefon</span><input value={personForm.phone ?? ""} onChange={(event) => setPersonForm({ ...personForm, phone: event.target.value })}/></label></div><div className="form-actions"><button className="primary" disabled={busy || !personForm.name?.trim()} onClick={savePerson}><Save size={15}/>Kaydet</button>{personForm.id && <button className="secondary" onClick={() => setPersonForm({ ...emptyPerson })}>Vazgeç</button>}</div></div>}
      </section>
      <section className="panel inventory-card"><div className="panel-top"><Laptop size={18}/><h3>Cihazlar</h3></div>
        <div className="inventory-list">{devices.length ? devices.map((device) => <button key={device.id} className="inventory-row" onClick={() => user.role === "admin" && setDeviceForm({ ...device })}><span className="device-icon"><Cpu size={17}/></span><span><strong>{device.name}</strong><small>{kindNames[device.kind]} · {device.personName || "Sahibi yok"}{device.ipAddress ? ` · ${device.ipAddress}` : ""}</small></span></button>) : <p className="quiet-state">Henüz cihaz kaydı yok.</p>}</div>
        {user.role === "admin" && <div className="inventory-form"><h4>{deviceForm.id ? "Cihazı düzenle" : "Cihaz ekle"}</h4><div className="form-row two"><label className="control"><span>Cihaz adı</span><input placeholder="LT-042" value={deviceForm.name} onChange={(event) => setDeviceForm({ ...deviceForm, name: event.target.value })}/></label><label className="control"><span>Tür</span><select value={deviceForm.kind} onChange={(event) => setDeviceForm({ ...deviceForm, kind: event.target.value as Device["kind"] })}>{Object.entries(kindNames).map(([kind, name]) => <option value={kind} key={kind}>{name}</option>)}</select></label></div><div className="form-row two"><label className="control"><span>Kullanan kişi</span><select value={deviceForm.personId ?? ""} onChange={(event) => setDeviceForm({ ...deviceForm, personId: event.target.value || null })}><option value="">Atanmadı</option>{people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}</select></label><label className="control"><span>Bağlı cihaz / ağ düğümü</span><select value={deviceForm.parentId ?? ""} onChange={(event) => setDeviceForm({ ...deviceForm, parentId: event.target.value || null })}><option value="">Bağımsız</option>{devices.filter((device) => device.id !== deviceForm.id).map((device) => <option key={device.id} value={device.id}>{device.name}</option>)}</select></label></div><div className="form-row two"><label className="control"><span>İç IP</span><input value={deviceForm.ipAddress ?? ""} onChange={(event) => setDeviceForm({ ...deviceForm, ipAddress: event.target.value })}/></label><label className="control"><span>Seri / envanter no</span><input value={deviceForm.serial ?? ""} onChange={(event) => setDeviceForm({ ...deviceForm, serial: event.target.value })}/></label></div><label className="control"><span>Konum</span><input value={deviceForm.location ?? ""} onChange={(event) => setDeviceForm({ ...deviceForm, location: event.target.value })}/></label><label className="control"><span>Not</span><textarea rows={2} value={deviceForm.notes ?? ""} onChange={(event) => setDeviceForm({ ...deviceForm, notes: event.target.value })}/></label><div className="form-actions"><button className="primary" disabled={busy || !deviceForm.name?.trim()} onClick={saveDevice}><Save size={15}/>Kaydet</button>{deviceForm.id && <button className="secondary" onClick={() => setDeviceForm({ ...emptyDevice })}>Vazgeç</button>}</div></div>}
      </section>
    </div>
    <section className="panel topology-panel"><div className="panel-top"><Network size={18}/><div><h3>Ağ topolojisi</h3><p>Cihazların “bağlı cihaz” ilişkisi burada gösterilir.</p></div></div><Topology devices={devices}/></section>
  </div>;
}

function Topology({ devices }: { devices: Device[] }) {
  if (!devices.length) return <p className="quiet-state">Topoloji için önce cihaz ekleyin.</p>;
  const depth = (device: Device, seen = new Set<string>()): number => {
    if (!device.parentId || seen.has(device.id)) return 0;
    const parent = devices.find((item) => item.id === device.parentId);
    return parent ? Math.min(5, 1 + depth(parent, new Set([...seen, device.id]))) : 0;
  };
  const columns = new Map<number, Device[]>();
  devices.forEach((device) => { const level = depth(device); columns.set(level, [...(columns.get(level) ?? []), device]); });
  const positions = new Map<string, { x: number; y: number }>();
  columns.forEach((items, level) => items.forEach((device, index) => positions.set(device.id, { x: 24 + level * 205, y: 24 + index * 84 })));
  const width = Math.max(420, (Math.max(...columns.keys()) + 1) * 205 + 50);
  const height = Math.max(150, Math.max(...Array.from(columns.values()).map((items) => items.length)) * 84 + 32);
  return <div className="topology-scroll"><svg width={width} height={height} role="img" aria-label="Cihaz bağlantı topolojisi">
    {devices.map((device) => { const child = positions.get(device.id); const parent = device.parentId ? positions.get(device.parentId) : null; return child && parent ? <path key={`line-${device.id}`} d={`M${parent.x + 166} ${parent.y + 28} C${parent.x + 185} ${parent.y + 28},${child.x - 20} ${child.y + 28},${child.x} ${child.y + 28}`} fill="none" stroke="#9aa9bf" strokeWidth="2"/> : null; })}
    {devices.map((device) => { const at = positions.get(device.id)!; return <g key={device.id}><rect x={at.x} y={at.y} width="166" height="56" rx="10" fill="#f7f9fd" stroke="#ccd6e6"/><text x={at.x + 12} y={at.y + 22} fontSize="13" fontWeight="700" fill="#263650">{device.name.slice(0, 20)}</text><text x={at.x + 12} y={at.y + 42} fontSize="11" fill="#6b7890">{(device.ipAddress || kindNames[device.kind]).slice(0, 24)}</text></g>; })}
  </svg></div>;
}
