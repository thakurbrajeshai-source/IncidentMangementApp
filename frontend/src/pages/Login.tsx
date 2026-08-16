import { useState } from "react";
import { api, tokenStore } from "../api";

// 3-step login: mobile -> OTP -> (if first-time) First/Last/Email -> in.

export function Login() {
  const [mobile, setMobile] = useState("+91 90000 00099"); // prefill an admin for easy testing
  const [otp, setOtp] = useState("");
  const [devOtp, setDevOtp] = useState<string | null>(null);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [stage, setStage] = useState<"mobile" | "otp" | "register">("mobile");

  async function requestOtp() {
    setError(null); setBusy(true);
    try {
      const r = await api.requestOtp(mobile);
      setDevOtp(r.devOtp);
      if (r.devOtp) setOtp(r.devOtp); // autofill in dev
      setStage("otp");
    } catch (e: any) { setError(e.message); } finally { setBusy(false); }
  }

  async function verify() {
    setError(null); setBusy(true);
    try {
      console.log("Verifying OTP for mobile:", mobile);
      const r = await api.verifyOtp(mobile, otp);
      console.log("Verify response:", r);
      if (r.isNewUser) { 
        console.log("New user detected, showing registration form");
        setStage("register"); 
        setBusy(false); 
        return; 
      }
      console.log("Existing user, logging in");
      tokenStore.set(r.accessToken);
      tokenStore.setUser(r.user);
      location.href = r.user.role === "Admin" ? "/admin" : r.user.role === "Resolver" ? "/resolver" : "/";
    } catch (e: any) { 
      console.error("Verify error:", e); 
      setError(e.message); 
    } finally { setBusy(false); }
  }

  async function register() {
    setError(null);
    
    // Quick validation - just check if fields are filled
    if (!firstName.trim() || !lastName.trim() || !email.trim()) { 
      setError("All fields are required"); 
      return; 
    }
    
    console.log("Registering new user:", { mobile, firstName, lastName, email });
    setBusy(true);
    try {
      const r = await api.verifyOtp(mobile, otp, { firstName, lastName, email });
      console.log("Registration response:", r);
      tokenStore.set(r.accessToken);
      tokenStore.setUser(r.user);
      location.href = "/";
    } catch (e: any) { 
      console.error("Registration error:", e); 
      setError(e.message); 
    } finally { setBusy(false); }
  }

  return (
    <div className="login-shell">
      <h1>Incident Management</h1>
      {stage === "mobile" && (
        <div className="step">
          <label>Mobile number</label>
          <input value={mobile} onChange={e => setMobile(e.target.value)} placeholder="+91 98xxx xxxxx" />
          <div className="actions" style={{ marginTop: 12 }}>
            <button className="primary" onClick={requestOtp} disabled={busy}>Send OTP</button>
          </div>
          {error && <div className="error">{error}</div>}
          <p style={{ fontSize: 11, color: "var(--text-soft)", marginTop: 12 }}>
            Dev OTP is <code>123456</code>. Pre-filled with a seeded admin for quick testing.
          </p>
        </div>
      )}
      {stage === "otp" && (
        <div className="step">
          <label>Enter OTP</label>
          <input value={otp} onChange={e => setOtp(e.target.value)} placeholder="123456" maxLength={6} />
          {devOtp && <p style={{ fontSize: 11, color: "var(--text-soft)" }}>Dev mode: OTP is {devOtp}</p>}
          <div className="actions" style={{ marginTop: 12, display: "flex", gap: 8 }}>
            <button onClick={() => setStage("mobile")}>Back</button>
            <button className="primary" onClick={verify} disabled={busy}>Verify</button>
          </div>
          {error && <div className="error">{error}</div>}
        </div>
      )}
      {stage === "register" && (
        <div className="step">
          <p style={{ fontSize: 12, color: "var(--text-soft)" }}>First time here — please complete your profile.</p>
          <label style={{ marginTop: 8 }}>First name *</label>
          <input value={firstName} onChange={e => setFirstName(e.target.value)} placeholder="John" disabled={busy} />
          <label style={{ marginTop: 8 }}>Last name *</label>
          <input value={lastName} onChange={e => setLastName(e.target.value)} placeholder="Doe" disabled={busy} />
          <label style={{ marginTop: 8 }}>Email *</label>
          <input value={email} onChange={e => setEmail(e.target.value)} type="email" placeholder="john@example.com" disabled={busy} />
          <div className="actions" style={{ marginTop: 12, display: "flex", gap: 8 }}>
            <button onClick={() => { setStage("otp"); setError(null); }} disabled={busy}>Back</button>
            <button className="primary" onClick={register} disabled={busy}>Create account</button>
          </div>
          {error && <div className="error">{error}</div>}
        </div>
      )}
    </div>
  );
}
