import type { ReactNode } from "react";

type StatusCardProps = {
  variant?: 'success' | 'error';
  title: string;
  message: string;
  children?: ReactNode;
}

export function StatusCard({
  children,
  variant = 'success',
  title,
  message
}: Readonly<StatusCardProps>) {
  const cssClassName = variant === 'success' ? 'bg-green-100' : 'bg-red-100';
  const baseClass = "mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full";
  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
        <div className={`${baseClass} ${cssClassName}`}>
          {variant == 'success' ?
            <svg className="h-8 w-8 text-green-600" fill="none" viewBox="0 0 24 24"
              stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg> :
            <svg className="h-8 w-8 text-red-600" fill="none" viewBox="0 0 24 24"
              stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          }
        </div>
        <h1 className="text-2xl font-bold text-gray-900">{title}</h1>
        <p className="mt-3 text-gray-600">{message}</p>
        {children}
      </div>
    </div>
  );
};
