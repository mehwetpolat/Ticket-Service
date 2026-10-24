import { useState } from "react";
import { LogOut, ShieldCheck } from "lucide-react";
import { api } from "../lib/api";
import type { SessionUser } from "../lib/types";

export function AccountPanel({ user, onError }: { user: SessionUser; onError: (message: string) => void }) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  async function changePassword() {
    setBusy(true); onError("");
    try { await api.changePassword(currentPassword, newPassword); setCurrentPassword(""); setNewPassword(""); setNotice("Şifreniz değiştirildi."); }
    catch (error) { onError(error instanceof Error ? error.message : "Şifre değiştirilemedi."); }
    finally { setBusy(false); }
  }
  return <div className="content-width"><div className="page-intro"><div><span className="eyebrow">HESABIM</span><h2>Hesap ve güvenlik</h2><p>Kişisel hesabınızı ve oturumunuzu yönetin.</p></div></div><section className="panel account-card"><div className="panel-top"><ShieldCheck size={19}/><h3>{user.name}</h3></div><p>{user.email}</p>{notice && <p className="connection good">{notice}</p>}<div className="form-row two"><label className="control"><span>Mevcut şifre</span><input type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)}/></label><label className="control"><span>Yeni şifre (en az 12 karakter)</span><input type="password" autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)}/></label></div><div className="form-actions"><button className="primary" disabled={busy || !currentPassword || newPassword.length < 12} onClick={changePassword}>Şifreyi değiştir</button><button className="secondary" onClick={async () => { await api.logout(); window.location.reload(); }}><LogOut size={15}/>Çıkış yap</button></div></section></div>;
}
