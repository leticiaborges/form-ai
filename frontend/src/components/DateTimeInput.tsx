import { useState } from "react";
import { DateInput } from "./DateInput";
import { TimeInput } from "./TimeInput";

interface DateTimeInputProps {
  id: string;
  label: string;
  value: string; /* local clock  "YYYY-MM-DDTHH:mm". */
  onChange: (value: string) => void;
  onBlur?: () => void;
  error?: string;
  disabled?: boolean;
  minDate?: string;
  timeFormat?: "HH:mm" | "HH:mm:ss";
}

function splitValue(value: string) {
  const [date = "", time = ""] = value.split("T");
  return { date, time };
}

export function DateTimeInput({
  id,
  label,
  value,
  onChange,
  onBlur,
  error,
  disabled,
  minDate,
  timeFormat = "HH:mm",
}: Readonly<DateTimeInputProps>) {
  const [draft, setDraft] = useState(() => splitValue(value));
  const [prevVal, setPrevVal] = useState(value);

  if (value !== prevVal) {
    setPrevVal(value);
    if (value) setDraft(splitValue(value));
  }

  function update(newValue: { date: string; time: string }) {
    setDraft(newValue);
    onChange(newValue.date && newValue.time ? `${newValue.date}T${newValue.time}` : "");
  }

  return (
    <fieldset disabled={disabled} className="flex flex-col gap-1">
      <legend className="text-sm font-medium text-gray-700">{label}</legend>
      <div className="flex items-center gap-2">
        <div className="w-44">
          <DateInput
            id={`di-${id}`}
            aria-label="Date"
            value={draft.date}
            min={minDate}
            error={error}
            showError={false}
            onChange={(e) => update({ date: e.target.value, time: draft.time })}
            onBlur={onBlur}
          ></DateInput>
        </div>
        <div className="w-32">
          <TimeInput
            id={`ti-${id}`}
            aria-label="Time"
            value={draft.time}
            error={error}
            showError={false}
            onChange={(e) => update({ date: draft.date, time: e.target.value })}
            onBlur={onBlur}
            format={timeFormat}
          ></TimeInput>
        </div>
      </div>

      {error && <span className="text-xs text-red-500">{error}</span>}
    </fieldset>
  );
}
