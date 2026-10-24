import { useEffect, useState } from "react";
import { Check, ChevronRight, Folder, FolderPlus, HardDrive, LoaderCircle, MapPin, MonitorDown } from "lucide-react";
import { api } from "../lib/api";
import type { ModelStorage, ModelStorageSlot } from "../lib/types";

type BrowseSlot = Exclude<ModelStorageSlot, "custom">;
type FolderList = { slot: ModelStorageSlot; folder: string; directories: string[]; path: string };

export function ModelStoragePicker({ storage, onChanged }: { storage: ModelStorage; onChanged: () => Promise<void> }) {
  const [slot, setSlot] = useState<BrowseSlot>(storage.selection.slot === "custom" ? "primary" : storage.selection.slot);
  const [folder, setFolder] = useState(storage.selection.slot === "custom" ? "" : storage.selection.folder);
  const [listing, setListing] = useState<FolderList | null>(null);
  const [name, setName] = useState("");
  const [path, setPath] = useState(storage.selection.path);
  const [busy, setBusy] = useState("");
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    api.modelFolders(slot, folder).then((value) => { if (active) setListing(value); }).catch((reason) => { if (active) setError(reason instanceof Error ? reason.message : "Klasörler açılamadı."); });
    return () => { active = false; };
  }, [slot, folder]);

  useEffect(() => {
    setPath(storage.selection.path);
  }, [storage.selection.path]);

  const isSelected = slot === storage.selection.slot && folder === storage.selection.folder;
  const pathSelected = storage.selection.slot === "custom" && normalize(path) === normalize(storage.selection.path);
  const downloading = storage.job.state === "running";

  async function choose() {
    setBusy("choose");
    setError("");
    setNotice("");
    try {
      await api.selectModelStorage(slot, folder);
      await onChanged();
      setNotice("Model konumu kaydedildi.");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Konum değiştirilemedi."); }
    finally { setBusy(""); }
  }

  async function choosePath() {
    const value = path.trim();
    if (!value) { setError("Klasörün tam yolunu yazın."); return; }
    setBusy("path");
    setError("");
    setNotice("");
    try {
      await api.selectModelPath(value);
      await onChanged();
      setNotice("Tam konum kaydedildi. Model indirmesi bu klasörün altındaki models klasörüne yapılır.");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Konum değiştirilemedi."); }
    finally { setBusy(""); }
  }

  async function pick() {
    setBusy("pick");
    setError("");
    setNotice("");
    try {
      const result = await api.pickModelFolder(path.trim() || storage.selection.path);
      if (result.path) {
        setPath(result.path);
        setNotice("Klasör seçildi. Kaydetmek için “Bu konumu kullan” düğmesine basın.");
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Klasör seçici açılamadı. Tam yolu yazarak devam edebilirsiniz.");
    }
    finally { setBusy(""); }
  }

  async function createFolder() {
    setBusy("folder");
    setError("");
    setNotice("");
    try {
      const result = await api.createModelFolder(slot, folder, name);
      setListing(result);
      setName("");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Klasör oluşturulamadı."); }
    finally { setBusy(""); }
  }

  return <section className="panel setup-panel storage-picker">
    <div className="section-heading"><HardDrive size={21}/><div><h2>Model kayıt konumu</h2><p>Model dosyalarının indirileceği klasörü seçin. Seçilen klasörün altında <code>models</code> klasörü oluşturulur; eski konumdaki modeller silinmez.</p></div></div>
    <div className="storage-current"><Check size={16}/>Kullanılan konum: <strong>{storage.selection.path || storage.selection.label}</strong></div>
    {storage.selection.slot === "primary" && storage.roots.primaryHost && <p className="storage-host">Bilgisayardaki yol: <code>{storage.roots.primaryHost}</code></p>}
    {storage.selection.slot === "secondary" && storage.roots.secondaryHost && <p className="storage-host">Bilgisayardaki yol: <code>{storage.roots.secondaryHost}</code></p>}

    <div className="storage-exact">
      <div className="section-heading compact"><MapPin size={18}/><div><strong>Bilgisayardaki tam konum</strong><p>Klasörün tam yolunu yazın veya bilgisayardan seçin.</p></div></div>
      <div className="storage-path-row">
        <input aria-label="Model klasörünün tam yolu" placeholder={storage.roots.primary || "Örn. D:\\modeller\\ollama"} value={path} onChange={(event) => { setPath(event.target.value); setNotice(""); }} spellCheck={false}/>
        <button className="secondary" type="button" disabled={Boolean(busy) || downloading} onClick={pick}>{busy === "pick" ? <LoaderCircle className="spin" size={16}/> : <MonitorDown size={16}/>}Bilgisayardan seç</button>
        <button className="primary" type="button" disabled={Boolean(busy) || downloading || !path.trim() || pathSelected} onClick={choosePath}>{busy === "path" ? <LoaderCircle className="spin" size={16}/> : <Check size={16}/>}Bu konumu kullan</button>
      </div>
      <p className="setting-help">Docker ile çalışıyorsanız klasörün hem TickeTask hem Ollama konteynerine bağlı olması gerekir. Bu durumda <code>.env</code> içindeki <code>TICKETASK_MODEL_PRIMARY_ROOT</code> veya <code>TICKETASK_MODEL_SECONDARY_ROOT</code> değerini istediğiniz klasöre ayarlayıp <code>docker compose up -d</code> çalıştırın; ardından aşağıdaki konumlardan klasörü seçin. Klasörün bilgisayardaki yolunu (<code>D:\modeller\ollama</code> gibi) yazacaksanız <code>.env</code> içindeki <code>TICKETASK_MODEL_PRIMARY_HOST_PATH</code> ve <code>TICKETASK_MODEL_SECONDARY_HOST_PATH</code> değerlerini de doldurun; yazdığınız yol otomatik olarak konteyner yoluna çevrilir. Docker dışında çalışırken klasör seçici doğrudan açılır.</p>
    </div>

    <div className="storage-roots">
      {(["primary", "secondary"] as BrowseSlot[]).map((key) => <button key={key} type="button" className={slot === key ? "selected" : ""} onClick={() => { setSlot(key); setFolder(""); setError(""); setNotice(""); }}><HardDrive size={17}/>{storage.roots[key]}</button>)}
      {storage.roots.custom && <span className="storage-custom-root"><MapPin size={15}/>{storage.roots.custom}</span>}
    </div>
    <div className="storage-browser">
      <div className="storage-breadcrumb"><button type="button" disabled={!folder} onClick={() => setFolder(folder.split("/").slice(0, -1).join("/"))}>Üst klasör</button><span>{listing?.path ?? `${storage.roots[slot]}${folder ? `/${folder}` : ""}`}</span></div>
      <div className="storage-folders">{listing?.directories.length ? listing.directories.map((directory) => <button type="button" key={directory} onClick={() => setFolder([folder, directory].filter(Boolean).join("/"))}><Folder size={17}/>{directory}<ChevronRight size={15}/></button>) : <span>Bu konumda alt klasör yok.</span>}</div>
      <div className="storage-create"><input aria-label="Yeni klasör adı" placeholder="Yeni klasör adı" value={name} onChange={(event) => setName(event.target.value)}/><button className="secondary" type="button" disabled={Boolean(busy) || !name.trim()} onClick={createFolder}><FolderPlus size={16}/>Oluştur</button></div>
    </div>
    {notice && <p className="connection good">{notice}</p>}
    {error && <p className="item-error">{error}</p>}
    <div className="setting-actions"><button className="primary" type="button" disabled={Boolean(busy) || isSelected || downloading} onClick={choose}>{busy === "choose" ? <LoaderCircle className="spin" size={16}/> : <Check size={16}/>}Bu klasörü kullan</button>{isSelected && <span className="connection good">Seçili konum</span>}</div>
  </section>;
}

function normalize(value: string) {
  return value.trim().replace(/\\/g, "/").replace(/\/+$/, "").toLocaleLowerCase("tr");
}
