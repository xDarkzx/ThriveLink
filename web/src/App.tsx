import { useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, Clock, Footprints, RefreshCw, Users, Watch } from "lucide-react";
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  LabelList,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { fetchStats, type Provider, type Stats } from "./api";

// Chart tokens (dataviz reference palette): one series hue, recessive chrome, ink for text.
const SERIES = "#2a78d6";
const GRID = "#e1e0d9";
const AXIS = "#898781";
const STATUS = { good: "#0ca30c", warning: "#fab219", critical: "#d03b3b" } as const;

const PROVIDER_LABEL: Record<Provider, string> = {
  AppleHealth: "Apple Health",
  Fitbit: "Fitbit",
  Garmin: "Garmin",
  Samsung: "Samsung Health",
};

const nzDate = (iso: string) => new Date(iso).toLocaleDateString("en-NZ", { day: "numeric", month: "short" });
const tooltipStyle = { background: "#fcfcfb", border: "1px solid rgba(11,11,11,0.10)", borderRadius: 8, fontSize: 12 };

export default function App() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const ctrl = new AbortController();
    fetchStats(ctrl.signal)
      .then((s) => {
        setStats(s);
        setError(null);
      })
      .catch((e: unknown) => {
        if (!ctrl.signal.aborted) setError(e instanceof Error ? e.message : "Failed to load stats");
      });
    return () => {
      ctrl.abort();
    };
  }, [reloadKey]);

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto max-w-7xl px-4 py-4 flex items-center justify-between gap-4">
          <div>
            <h1 className="text-xl font-bold">
              <span className="text-brand">Thrive</span>Link
            </h1>
            <p className="text-xs text-slate-500">Wellness programme operations · synthetic demo data</p>
          </div>
          <button
            onClick={() => {
              setReloadKey((k) => k + 1);
            }}
            className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-50"
          >
            <RefreshCw className="size-4" /> Refresh
          </button>
        </div>
      </header>

      <main className="mx-auto max-w-7xl px-4 py-6 space-y-6">
        {error && <p className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>}
        {!stats && !error && <p className="text-slate-500">Loading…</p>}
        {stats && <Dashboard stats={stats} />}
      </main>
    </div>
  );
}

function Dashboard({ stats }: { stats: Stats }) {
  const linkedPct = stats.members.total ? Math.round((100 * stats.members.withDevice) / stats.members.total) : 0;
  const syncTotal = stats.syncHealth.fresh + stats.syncHealth.delayed + stats.syncHealth.stale;

  return (
    <>
      <section className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatTile icon={<Users className="size-5 text-slate-500" />} label="Members" value={stats.members.total} />
        <StatTile
          icon={<Watch className="size-5 text-slate-500" />}
          label="Devices linked"
          value={`${linkedPct}%`}
          note={`${stats.members.withDevice} of ${stats.members.total}`}
        />
        <StatTile
          icon={<CheckCircle2 className="size-5 text-slate-500" />}
          label="Hit weekly target last week"
          value={`${stats.lastWeek.hitRate}%`}
          note={`${stats.lastWeek.membersHitTarget} members · week of ${nzDate(stats.lastWeek.weekStart)}`}
        />
        <StatTile
          icon={<Footprints className="size-5 text-slate-500" />}
          label="Avg steps yesterday"
          value={(stats.dailyActivity.at(-1)?.averageSteps ?? 0).toLocaleString("en-NZ")}
        />
      </section>

      <section className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <Card title="Average daily steps · last 14 days" className="lg:col-span-2">
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={stats.dailyActivity} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid stroke={GRID} vertical={false} />
                <XAxis dataKey="date" tickFormatter={nzDate} stroke={AXIS} fontSize={11} tickLine={false} />
                <YAxis stroke={AXIS} fontSize={11} tickLine={false} axisLine={false} width={48} />
                <Tooltip
                  contentStyle={tooltipStyle}
                  labelFormatter={(d) => (typeof d === "string" ? nzDate(d) : "")}
                  formatter={(v) => [Number(v).toLocaleString("en-NZ"), "Avg steps"]}
                />
                <Area
                  type="monotone"
                  dataKey="averageSteps"
                  stroke={SERIES}
                  strokeWidth={2}
                  fill={SERIES}
                  fillOpacity={0.12}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card title="Device sync health">
          <ul className="space-y-3">
            <SyncRow
              color={STATUS.good}
              icon={<CheckCircle2 className="size-4" />}
              label="Synced in last 24h"
              count={stats.syncHealth.fresh}
              total={syncTotal}
            />
            <SyncRow
              color={STATUS.warning}
              icon={<Clock className="size-4" />}
              label="Delayed 1–3 days"
              count={stats.syncHealth.delayed}
              total={syncTotal}
            />
            <SyncRow
              color={STATUS.critical}
              icon={<AlertTriangle className="size-4" />}
              label="Not synced 3+ days"
              count={stats.syncHealth.stale}
              total={syncTotal}
            />
          </ul>
          <p className="mt-4 text-xs text-slate-500">
            Stale devices are the usual cause of "my points didn't show up" calls.
          </p>
        </Card>
      </section>

      <section className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card title="Members by status tier">
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={stats.statusCounts} layout="vertical" margin={{ top: 4, right: 40, left: 8, bottom: 4 }}>
                <XAxis type="number" hide />
                <YAxis
                  type="category"
                  dataKey="status"
                  stroke={AXIS}
                  fontSize={12}
                  tickLine={false}
                  axisLine={false}
                  width={72}
                />
                <Tooltip
                  contentStyle={tooltipStyle}
                  cursor={{ fill: "rgba(11,11,11,0.04)" }}
                  formatter={(v, _n, item) => [
                    `${String(v)} members · ${String((item.payload as { discountPercent: number }).discountPercent)}% premium discount`,
                    "",
                  ]}
                />
                <Bar dataKey="count" fill={SERIES} radius={[0, 4, 4, 0]} barSize={22}>
                  <LabelList dataKey="count" position="right" fill="#52514e" fontSize={12} />
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card title="Linked devices by provider">
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={stats.devicesByProvider.map((d) => ({ ...d, label: PROVIDER_LABEL[d.provider] }))}
                layout="vertical"
                margin={{ top: 4, right: 40, left: 8, bottom: 4 }}
              >
                <XAxis type="number" hide />
                <YAxis
                  type="category"
                  dataKey="label"
                  stroke={AXIS}
                  fontSize={12}
                  tickLine={false}
                  axisLine={false}
                  width={110}
                />
                <Tooltip
                  contentStyle={tooltipStyle}
                  cursor={{ fill: "rgba(11,11,11,0.04)" }}
                  formatter={(v) => [`${String(v)} devices`, ""]}
                />
                <Bar dataKey="count" fill={SERIES} radius={[0, 4, 4, 0]} barSize={22}>
                  <LabelList dataKey="count" position="right" fill="#52514e" fontSize={12} />
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>
      </section>

      <section className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card title="Top members this year">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-slate-500 border-b border-slate-200">
                <th className="py-2 font-medium">Member</th>
                <th className="py-2 font-medium">Status</th>
                <th className="py-2 font-medium text-right">Points</th>
                <th className="py-2 font-medium text-right">Discount</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {stats.topMembers.map((m) => (
                <tr key={m.memberNumber}>
                  <td className="py-2">
                    {m.name} <span className="text-xs text-slate-400 font-mono">{m.memberNumber}</span>
                  </td>
                  <td className="py-2">{m.status}</td>
                  <td className="py-2 text-right tabular-nums">{m.points.toLocaleString("en-NZ")}</td>
                  <td className="py-2 text-right tabular-nums">{m.discountPercent}%</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>

        <Card title={`Flagged readings · last 30 days (${stats.flaggedReadings.length})`}>
          {stats.flaggedReadings.length === 0 ? (
            <p className="text-sm text-slate-500">None.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-slate-500 border-b border-slate-200">
                  <th className="py-2 font-medium">Date</th>
                  <th className="py-2 font-medium">Member</th>
                  <th className="py-2 font-medium">Device</th>
                  <th className="py-2 font-medium">Reason</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {stats.flaggedReadings.slice(0, 8).map((f) => (
                  <tr key={`${f.memberNumber}-${f.date}`}>
                    <td className="py-2 whitespace-nowrap">{nzDate(f.date)}</td>
                    <td className="py-2 font-mono text-xs">{f.memberNumber}</td>
                    <td className="py-2">{PROVIDER_LABEL[f.provider]}</td>
                    <td className="py-2 text-slate-600">{f.flagReason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="mt-3 text-xs text-slate-500">Points are withheld on flagged days until reviewed.</p>
        </Card>
      </section>

      <p className="text-xs text-slate-400">Generated {new Date(stats.generatedAt).toLocaleString("en-NZ")}</p>
    </>
  );
}

function Card({ title, className = "", children }: { title: string; className?: string; children: React.ReactNode }) {
  return (
    <div className={`rounded-xl border border-slate-200 bg-[#fcfcfb] p-4 ${className}`}>
      <h2 className="mb-3 text-sm font-semibold text-slate-700">{title}</h2>
      {children}
    </div>
  );
}

function StatTile({
  icon,
  label,
  value,
  note,
}: {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  note?: string;
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      {icon}
      <p className="mt-2 text-2xl font-bold">{value}</p>
      <p className="text-xs text-slate-500">{label}</p>
      {note && <p className="mt-1 text-xs text-slate-400">{note}</p>}
    </div>
  );
}

function SyncRow({
  color,
  icon,
  label,
  count,
  total,
}: {
  color: string;
  icon: React.ReactNode;
  label: string;
  count: number;
  total: number;
}) {
  const pct = total ? (100 * count) / total : 0;
  return (
    <li>
      <div className="flex items-center justify-between text-sm">
        <span className="flex items-center gap-2" style={{ color }}>
          {icon}
          <span className="text-slate-700">{label}</span>
        </span>
        <span className="tabular-nums font-semibold">{count}</span>
      </div>
      <div className="mt-1 h-2 rounded-full bg-slate-100 overflow-hidden">
        <div className="h-full rounded-full" style={{ width: `${pct}%`, backgroundColor: color }} />
      </div>
    </li>
  );
}
