import { Bar, BarChart, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { BarRectangleItem } from "recharts";
import type { FormSummary } from "../types/form";
import { BAR_COLOR } from "./charts/chartColors";

type SubmissionsPerFormChartProps = {
  forms: FormSummary[];
  onSelectForm: (formId: string) => void;
};

const TOP_FORMS = 5;
const ROW_HEIGHT = 44;
const CHART_PADDING = 16;
const Y_AXIS_WIDTH = 160;
const TITLE_MAX_CHARS = 24;
const BAR_RADIUS: [number, number, number, number] = [0, 4, 4, 0];

type ChartDatum = {
  id: string;
  title: string;
  count: number;
};

export function SubmissionsPerFormChart({
  forms,
  onSelectForm,
}: Readonly<SubmissionsPerFormChartProps>) {
  if (forms.length === 0) return null;

  const hasSubmissions = forms.some((form) => form.submissionCount > 0);

  if (!hasSubmissions) {
    return (
      <section className="bg-white rounded-2xl shadow-md p-6" aria-label="Submissions per form">
        <p className="py-8 text-center text-gray-400">No submissions yet.</p>
      </section>
    );
  }

  const data: ChartDatum[] = [...forms]
    .sort((a, b) => b.submissionCount - a.submissionCount)
    .slice(0, TOP_FORMS)
    .map((form) => ({ id: form.id, title: form.title, count: form.submissionCount }));

  const titleById = new Map(data.map((datum) => [datum.id, datum.title]));

  const max = Math.max(1, ...data.map((datum) => datum.count));

  const handleBarClick = (bar: BarRectangleItem) => onSelectForm((bar.payload as ChartDatum).id);

  const truncate = (title: string) =>
    title.length > TITLE_MAX_CHARS ? `${title.slice(0, TITLE_MAX_CHARS)}…` : title;

  return (
    <section className="bg-white rounded-2xl shadow-md p-6" aria-label="Submissions per form">
      {forms.length > TOP_FORMS && (
        <p className="mb-3 text-xs text-gray-500">
          Top {TOP_FORMS} of {forms.length} forms
        </p>
      )}

      <ResponsiveContainer width="100%" height={data.length * ROW_HEIGHT + CHART_PADDING}>
        <BarChart data={data} layout="vertical" margin={{ top: 0, right: 40, bottom: 0, left: 0 }}>
          <XAxis type="number" domain={[0, max]} hide />
          <YAxis
            type="category"
            dataKey="id"
            width={Y_AXIS_WIDTH}
            axisLine={false}
            tickLine={false}
            tick={{ fontSize: 12, fill: "#6b7280" }}
            tickFormatter={(id: string) => truncate(titleById.get(id) ?? "")}
          />
          <Tooltip
            cursor={{ fill: "rgba(0, 0, 0, 0.04)" }}
            labelFormatter={(id) => titleById.get(id as string) ?? ""}
            formatter={(value) => [value, Number(value) === 1 ? "Submission" : "Submissions"]}
            labelStyle={{ fontSize: "12px", fontWeight: "bold" }}
            itemStyle={{ color: "#6b6375", fontSize: "14px" }}
          />

          <Bar
            dataKey="count"
            fill={BAR_COLOR}
            radius={BAR_RADIUS}
            barSize={22}
            cursor="pointer"
            onClick={handleBarClick}
          >
            <LabelList dataKey="count" position="right" style={{ fontSize: 12, fill: "#6b6375" }} />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </section>
  );
}
