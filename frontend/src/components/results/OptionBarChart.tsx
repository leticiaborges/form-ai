import { Bar, BarChart, LabelList, Rectangle, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { BarShapeProps } from "recharts";
import type { OptionResult } from "../../types/results";

interface OptionBarChartProps {
  options: OptionResult[];
  answerCount: number;
}

const BAR_COLOR = '#0091b8';   // brand-600, from index.css
const CORRECT_COLOR = '#16a34a';

const ROW_HEIGHT = 34;
const CHART_PADDING = 16;
const BAR_RADIUS: [number, number, number, number] = [0, 4, 4, 0];

interface BarDatum {
  text: string;
  count: number;
  isCorrect: boolean;
  label: string;
}

function renderBar({ x, y, width, height, payload }: BarShapeProps) {
  const { isCorrect } = payload as BarDatum;

  return (
    <Rectangle
      x={x}
      y={y}
      width={width}
      height={height}
      radius={BAR_RADIUS}
      fill={isCorrect ? CORRECT_COLOR : BAR_COLOR}
    />
  );
}

export function OptionBarChart({ options, answerCount }: Readonly<OptionBarChartProps>) {

  const data: BarDatum[] = options.map((option) => {
    const percentage = answerCount > 0 ? Math.round((option.count / answerCount) * 100) : 0;
    const isCorrect = option.isCorrect === true;

    return {
      text: isCorrect ? `${option.text} ✓` : option.text,
      count: option.count,
      isCorrect,
      label: `${option.count} (${percentage}%)`
    };
  });

  const max = Math.max(1, ...data.map(option => option.count));

  return (
    <ResponsiveContainer width="100%" height={data.length * ROW_HEIGHT + CHART_PADDING}>
      <BarChart
        data={data}
        layout="vertical"
        margin={{ top: 0, right: 56, bottom: 0, left: 0 }}
      >
        <XAxis type="number" domain={[0, max]} hide />
        <YAxis
          type="category"
          dataKey="text"
          width={140}
          axisLine={false}
          tickLine={false}
          tick={{ fontSize: 12, fill: '#6b7280' }}
          tickFormatter={(text: string) =>
            text.length > 22 ? `${text.slice(0, 21)}…` : text}
        />
        <Tooltip
          cursor={{ fill: 'rgba(0, 0, 0, 0.04)' }}
          formatter={(value) => [value, 'answers']}
        />
        <Bar dataKey="count" barSize={18} shape={renderBar}>
          <LabelList
            dataKey="label"
            position="right"
            style={{ fontSize: 12, fill: '#374151' }}
          />
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}