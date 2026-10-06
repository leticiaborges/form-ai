import type { ReactNode } from "react";

interface LoadingOverlayProps {
  isLoading: boolean;
  message: string;
  hint?: string;
  className?: string;
  children: ReactNode;
}

export function LoadingOverlay({
  isLoading,
  message,
  hint,
  className = "",
  children,
}: Readonly<LoadingOverlayProps>) {
  return (
    <div className={`relative ${className}`}>
      <div inert={isLoading} aria-busy={isLoading} className={isLoading ? "opacity-60" : ""}>
        {children}
      </div>

      {/* Outside the inert content so it is still announced; always rendered so the live region exists before its text changes. */}
      <p role="status" aria-live="polite" className="sr-only">
        {isLoading ? message : ""}
      </p>

      {isLoading && (
        <div
          aria-hidden="true"
          className="absolute inset-0 z-10 flex flex-col items-center justify-center gap-3 rounded-[inherit] bg-white/70"
        >
          <svg className="animate-spin h-12 w-12 text-brand-600" viewBox="0 0 24 24" fill="none">
            <circle
              className="opacity-25"
              cx="12"
              cy="12"
              r="10"
              stroke="currentColor"
              strokeWidth="4"
            />
            <path
              className="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"
            />
          </svg>
          <p className="text-sm font-medium text-gray-800">{message}</p>
          {hint && <p className="text-xs text-gray-500">{hint}</p>}
        </div>
      )}
    </div>
  );
}
