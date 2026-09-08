import { Bar, BarChart, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { ScoreBucket } from "../../types/results";
import { BAR_COLOR } from "../charts/chartColors";

interface ScoreDistributionChartProps {
  buckets: ScoreBucket[];
  totalPoints: number | null;
}

export function ScoreDistributionChart({ buckets, totalPoints }: Readonly<ScoreDistributionChartProps>) {


  return (
    <section className="bg-white rounded-2xl shadow-md p-6 flex flex-col gap-1">
      <h3 className="text-base font-medium text-gray-900">Distribution of points</h3>

      <p className="mb-3 text-xs text-gray-500">
        {totalPoints === null
          ? 'Points earned per submission'
          : `Out of ${totalPoints} point${totalPoints !== 1 ? 's' : ''} available`}
      </p>

      <ResponsiveContainer width="100%" height={220}>
        <BarChart data={buckets} margin={{ top: 20, right: 8, bottom: 4, left: 0 }}>
          <XAxis
            dataKey="score"
            type="category"
            tickLine={false}
            axisLine={{ stroke: '#e5e7eb' }}
            tick={{ fontSize: 12, fill: '#6b7280' }}
            label={{
              value: 'Points', position: 'insideBottom', offset: -4,
              style: { fontSize: 11, fill: '#9ca3af' }
            }}
          />
          <YAxis
            allowDecimals={false}
            width={32}
            tickLine={false}
            axisLine={false}
            tick={{ fontSize: 12, fill: '#6b7280' }}
          />
          <Tooltip
            cursor={{ fill: 'rgba(0, 0, 0, 0.04)' }}
            labelFormatter={(score) => `${score} points`}
            formatter={(value) => [value, 'Submissions']}
            labelStyle={{ fontSize: '12px', fontWeight: 'bold' }}
            itemStyle={{ color: '#6b6375', fontSize: '14px' }}
          />
          <Bar dataKey="submissionCount" fill={BAR_COLOR} radius={[4, 4, 0, 0]} maxBarSize={56}>
            <LabelList
              dataKey="submissionCount"
              position="top"
              style={{ fontSize: 12, fill: '#374151' }}
            />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </section>
  );
}