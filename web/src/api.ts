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

export async function fetchStats(signal?: AbortSignal): Promise<Stats> {
  const res = await fetch("/api/stats", { signal });
  if (!res.ok) throw new Error(`Stats request failed: ${res.status}`);
  return (await res.json()) as Stats;
}
