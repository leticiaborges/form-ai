import { type InputHTMLAttributes, type Ref } from "react";

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  ref?: Ref<HTMLInputElement>
}

export function Input({ label, error, id, className = '', ref, ...props }: InputProps) {
  const inputId = id ?? label.toLowerCase().replaceAll(/\s+/g, '-');

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={inputId} className="text-sm font-medium text-gray-700">
        {label}
      </label>
      <input
        id={inputId}
        ref={ref}
        className={
          'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
          'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
          (error ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ') +
          className
        }
        {...props}
      />
      {error && <span className="text-xs text-red-500">{error}</span>}
    </div>
  );
}