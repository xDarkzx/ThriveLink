export type Provider = "AppleHealth" | "Fitbit" | "Garmin" | "Samsung";
export type Status = "Bronze" | "Silver" | "Gold" | "Platinum";

export interface Stats {
  generatedAt: string;
  members: { total: number; withDevice: number };
  devicesByProvider: { provider: Provider; count: number }[];
  statusCounts: { status: Status; count: number; discountPercent: number }[];
  lastWeek: { weekStart: string; membersHitTarget: number; hitRate: number };
  dailyActivity: { date: string; averageSteps: number; activeMembers: number }[];
  syncHealth: { fresh: number; delayed: number; stale: number };
  flaggedReadings: { memberNumber: string; date: string; steps: number; provider: Provider; flagReason: string }[];
  topMembers: { memberNumber: string; name: string; points: number; status: Status; discountPercent: number }[];
}

export type ClaimStatus = "Submitted" | "Triaged" | "NeedsInfo" | "Approved" | "Declined";
export type Recommendation = "LikelyApprove" | "Review" | "NeedsInfo";

export interface ClaimRow {
  claimNumber: string;
  member: string;
  type: "PreApproval" | "Reimbursement";
  category: string;
  providerName: string;
  treatmentDate: string;
  amount: number;
  status: ClaimStatus;
  dueBy: string;
  recommendation: Recommendation | null;
  ruleFlags: string | null;
  aiSummary: string | null;
  aiConfidence: number | null;
  documents: number;
}

export async function fetchClaims(signal?: AbortSignal): Promise<ClaimRow[]> {
  const res = await fetch("/api/claims", { signal });
  if (!res.ok) throw new Error(`Claims request failed: ${res.status}`);
  return (await res.json()) as ClaimRow[];
}

export async function decideClaim(
  claimNumber: string,
  decision: "Approved" | "Declined" | "NeedsInfo",
  assessor: string,
  note: string,
) {
  const res = await fetch(`/api/claims/${encodeURIComponent(claimNumber)}/decision`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ decision, assessor, note }),
  });
  if (!res.ok) throw new Error(`Decision failed: ${res.status}`);
}

export async function fetchStats(signal?: AbortSignal): Promise<Stats> {
  const res = await fetch("/api/stats", { signal });
  if (!res.ok) throw new Error(`Stats request failed: ${res.status}`);
  return (await res.json()) as Stats;
}
