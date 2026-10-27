import { useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, FileQuestion, Paperclip, Sparkles } from "lucide-react";
import { decideClaim, fetchClaims, type ClaimRow, type Recommendation } from "./api";

// Status palette (reserved): always paired with an icon and a label, never colour alone.
const RECOMMENDATION: Record<Recommendation, { label: string; color: string; Icon: typeof CheckCircle2 }> = {
  LikelyApprove: { label: "Likely approve", color: "#0ca30c", Icon: CheckCircle2 },
  Review: { label: "Review", color: "#d03b3b", Icon: AlertTriangle },
  NeedsInfo: { label: "Needs info", color: "#b7791f", Icon: FileQuestion },
};

const nzDate = (iso: string) => new Date(iso).toLocaleDateString("en-NZ", { day: "numeric", month: "short" });
const money = (n: number) => n.toLocaleString("en-NZ", { style: "currency", currency: "NZD" });
const isOpen = (c: ClaimRow) => c.status !== "Approved" && c.status !== "Declined";

export default function Claims() {
  const [claims, setClaims] = useState<ClaimRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [assessor, setAssessor] = useState("Demo Assessor");
  const [busy, setBusy] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const ctrl = new AbortController();
    fetchClaims(ctrl.signal)
      .then((c) => {
        setClaims(c);
        setError(null);
      })
      .catch((e: unknown) => {
        if (!ctrl.signal.aborted) setError(e instanceof Error ? e.message : "Failed to load claims");
      });
    return () => {
      ctrl.abort();
    };
  }, [reloadKey]);

  async function decide(claim: ClaimRow, decision: "Approved" | "Declined" | "NeedsInfo") {
    let note = "";
    if (decision !== "Approved") {
      const entered = window.prompt(
        decision === "Declined" ? "Reason for declining (required)" : "What information is needed?",
      );
      if (!entered?.trim()) return;
      note = entered.trim();
    }
    setBusy(claim.claimNumber);
    try {
      await decideClaim(claim.claimNumber, decision, assessor, note);
      setReloadKey((k) => k + 1);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Decision failed");
    } finally {
      setBusy(null);
    }
  }

  if (error) return <p className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>;
  if (!claims) return <p className="text-slate-500">Loading…</p>;

  const today = new Date().toISOString().slice(0, 10);
  const open = claims.filter(isOpen);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">Claims queue</h2>
          <p className="text-xs text-slate-500">
            {open.length} open · triage is advice only; every decision is made by an assessor
          </p>
        </div>
        <label className="text-xs text-slate-500">
          Assessor
          <input
            value={assessor}
            onChange={(e) => {
              setAssessor(e.target.value);
            }}
            className="ml-2 rounded border border-slate-200 px-2 py-1 text-sm text-slate-800"
          />
        </label>
      </div>

      <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-left text-xs text-slate-500">
              <th className="p-3 font-medium">Claim</th>
              <th className="p-3 font-medium">Member</th>
              <th className="p-3 font-medium">Provider</th>
              <th className="p-3 font-medium text-right">Amount</th>
              <th className="p-3 font-medium">Due</th>
              <th className="p-3 font-medium">Triage</th>
              <th className="p-3 font-medium">Decision</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 align-top">
            {claims.map((c) => {
              const rec = c.recommendation ? RECOMMENDATION[c.recommendation] : null;
              const overdue = isOpen(c) && c.dueBy < today;
              return (
                <tr key={c.claimNumber}>
                  <td className="p-3">
                    <div className="font-mono text-xs">{c.claimNumber}</div>
                    <div className="text-xs text-slate-500">
                      {c.type === "PreApproval" ? "Pre-approval" : "Reimbursement"} · {c.category}
                    </div>
                    <div className="mt-1 flex items-center gap-1 text-xs text-slate-400">
                      <Paperclip className="size-3" /> {c.documents}
                    </div>
                  </td>
                  <td className="p-3">{c.member}</td>
                  <td className="p-3">
                    {c.providerName}
                    <div className="text-xs text-slate-500">treated {nzDate(c.treatmentDate)}</div>
                  </td>
                  <td className="p-3 text-right tabular-nums">{money(c.amount)}</td>
                  <td className={`p-3 whitespace-nowrap ${overdue ? "font-semibold text-red-700" : ""}`}>
                    {nzDate(c.dueBy)}
                    {overdue && <div className="text-xs">overdue</div>}
                  </td>
                  <td className="p-3 max-w-sm">
                    {rec ? (
                      <span className="inline-flex items-center gap-1 font-medium" style={{ color: rec.color }}>
                        <rec.Icon className="size-4" />
                        <span className="text-slate-800">{rec.label}</span>
                      </span>
                    ) : (
                      <span className="text-xs text-slate-400">awaiting triage</span>
                    )}
                    {c.ruleFlags && <p className="mt-1 text-xs text-slate-600">{c.ruleFlags}</p>}
                    {c.aiSummary && (
                      <p className="mt-1 flex gap-1 text-xs text-slate-500">
                        <Sparkles className="size-3 shrink-0 mt-0.5" />
                        <span>
                          {c.aiSummary}
                          {c.aiConfidence !== null && ` (${Math.round(c.aiConfidence * 100)}%)`}
                        </span>
                      </p>
                    )}
                  </td>
                  <td className="p-3">
                    {isOpen(c) ? (
                      <div className="flex flex-col gap-1">
                        {(["Approved", "NeedsInfo", "Declined"] as const).map((d) => (
                          <button
                            key={d}
                            disabled={busy === c.claimNumber}
                            onClick={() => {
                              void decide(c, d);
                            }}
                            className="rounded border border-slate-200 px-2 py-1 text-xs hover:bg-slate-50 disabled:opacity-50"
                          >
                            {d === "Approved" ? "Approve" : d === "NeedsInfo" ? "Request info" : "Decline"}
                          </button>
                        ))}
                      </div>
                    ) : (
                      <span className="text-xs font-medium">{c.status}</span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
