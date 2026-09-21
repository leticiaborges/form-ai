import { type InputHTMLAttributes, type Ref } from "react";

interface TimeInputProps extends InputHTMLAttributes<HTMLInputElement> {
  id: string;
  error?: string;
  showError: boolean;
  format?: "HH:mm" | "HH:mm:ss";
  ref?: Ref<HTMLInputElement>;
}

export function TimeInput({
  error,
  id,
  showError = true,
  className = "",
  format,
  ref,
  ...props
}: Readonly<TimeInputProps>) {
  const step = format === "HH:mm:ss" ? 1 : 60;

  return (
    <>
      <input
        id={id}
        type="time"
        ref={ref}
        step={step}
        className={
          "w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none " +
          "focus:ring-2 focus:ring-brand-500 focus:border-brand-500 " +
          (error ? "border-red-400 focus:ring-red-400 " : "border-gray-300 ") +
          className
        }
        {...props}
      />
      {showError && error && <span className="text-xs text-red-500">{error}</span>}
    </>
  );
}
