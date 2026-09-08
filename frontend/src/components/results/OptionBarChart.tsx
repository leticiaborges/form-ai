import { Bar, BarChart, LabelList, Rectangle, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { BarShapeProps } from "recharts";
import type { OptionResult } from "../../types/results";
import { BAR_COLOR, CORRECT_COLOR, INCORRECT_COLOR } from "../charts/chartColors";

interface OptionBarChartProps {
  options: OptionResult[];
  answerCount: number;
}

const ROW_HEIGHT = 34;
const CHART_PADDING = 16;
const BAR_RADIUS: [number, number, number, number] = [0, 4, 4, 0];

interface BarDatum {
  text: string;
  count: number;
  isCorrect: boolean;
  label: string;
  isGraded: boolean;
}

function renderBar({ x, y, width, height, payload }: BarShapeProps) {
  const { isCorrect, isGraded } = payload as BarDatum;

  const fillColor = isGraded ? (isCorrect ? CORRECT_COLOR : INCORRECT_COLOR) : BAR_COLOR;

  return (
    <Rectangle
      x={x}
      y={y}
      width={width}
      height={height}
      radius={BAR_RADIUS}
      fill={fillColor}
    />
  );
}

export function OptionBarChart({ options, answerCount }: Readonly<OptionBarChartProps>) {

  const isGraded = options.some(option => option.isCorrect !== null);

  const data: BarDatum[] = options.map((option) => {
    const percentage = answerCount > 0 ? Math.round((option.count / answerCount) * 100) : 0;
    const isCorrect = option.isCorrect === true;

    return {
      text: isCorrect ? `${option.text} ✓` : option.text,
      count: option.count,
      isCorrect,
      label: `${option.count} (${percentage}%)`,
      isGraded: isGraded
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
            text.length > 30 ? `${text.slice(0, 30)}…` : text}
        />
        <Tooltip
          cursor={{ fill: 'rgba(0, 0, 0, 0.04)' }}
          formatter={(value) => [value, 'Answers']}
          labelStyle={{ fontSize: '12px', fontWeight: 'bold' }}
          itemStyle={{ color: '#6b6375', fontSize: '14px' }}
        />
        <Bar dataKey="count" barSize={18} shape={renderBar}>
          <LabelList
            dataKey="label"
            position="right"
            style={{ fontSize: 12, fill: '#6b6375' }}
          />
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}