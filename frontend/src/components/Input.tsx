import { forwardRef, type InputHTMLAttributes } from "react";

interface InputProps extends InputHTMLAttributes<HTMLInputElement>{
    label: string;
    error?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, id, className = '', ...props }, ref) => {
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
            'focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 ' +
            (error ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ') +
            className
          }
          {...props}
        />
        {error && <span className="text-xs text-red-500">{error}</span>}
      </div>
    );
  }
);

Input.displayName = 'Input';