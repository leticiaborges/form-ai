import type { ValueResult } from "../../types/results";

interface ValueAnswerListProps {
  values: ValueResult[];
}
const SCROLL_AFTER = 8;

export function ValueAnswerList({ values }: Readonly<ValueAnswerListProps>) {
  const scrolls = values.length > SCROLL_AFTER;

  return (
    <ul
      className={
        'flex flex-col divide-y divide-gray-100 rounded-lg border border-gray-100 ' +
        (scrolls ? 'max-h-72 overflow-y-auto' : '')
      }
    >
      {values.map(value => (
        <li
          key={value.value}
          className="flex items-center justify-between gap-3 px-3 py-2"
        >
          <span className="min-w-0 break-words text-sm text-gray-700">
            {value.value}
          </span>
          <span
            className="shrink-0 text-xs tabular-nums text-gray-400"
            title={`${value.count} respondent${value.count !== 1 ? 's' : ''} gave this answer`}
          >
            ({value.count})
          </span>
        </li>
      ))}
    </ul>
  );
}