import type { FormSummary } from "../types/form";

type SubmissionsPerFormChartProps = {
    forms: FormSummary[];
};

export function SubmissionsPerFormChart({ forms }: Readonly<SubmissionsPerFormChartProps>) {
    if (forms.length === 0) return null;

    const sorted = [...forms].sort((a, b) => b.submissionCount - a.submissionCount);
    const max = Math.max(1, ...sorted.map(f => f.submissionCount));

    return (
        <section className="bg-white rounded-2xl shadow-md p-6">
            <h2 className="text-lg font-semibold text-gray-900 mb-4">Submissions per form</h2>
            <ul className="flex flex-col gap-3">
                {sorted.map(form => (
                    <li
                        key={form.id}
                        className="flex items-center gap-3"
                        title={`${form.title}: ${form.submissionCount} submission${form.submissionCount !== 1 ? 's' : ''}`}
                    >
                        <span className="w-32 shrink-0 truncate text-sm text-gray-600">
                            {form.title}
                        </span>
                        <div className="flex-1 h-2.5 rounded-full bg-gray-100">
                            <div
                                className="h-2.5 rounded-full bg-brand-600"
                                style={{ width: `${(form.submissionCount / max) * 100}%` }}
                            />
                        </div>
                        <span className="w-8 shrink-0 text-right text-sm font-medium text-gray-900">
                            {form.submissionCount}
                        </span>
                    </li>
                ))}
            </ul>
        </section>
    );
}