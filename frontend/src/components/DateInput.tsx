import { type InputHTMLAttributes, type Ref } from "react";

interface DateInputProps extends InputHTMLAttributes<HTMLInputElement> {
  id: string;
  error?: string;
  showError?: boolean;
  ref?: Ref<HTMLInputElement>;
}

export function DateInput({
  error,
  id,
  showError = true,
  className = "",
  ref,
  ...props
}: Readonly<DateInputProps>) {
  return (
    <>
      <input
        id={id}
        type="date"
        ref={ref}
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
