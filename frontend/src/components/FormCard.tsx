import type { FormSummary } from "../types/form";

type FormCardProps = {
  form: FormSummary;
  onClick: () => void;
}

export function FormCard({
  form,
  onClick
}: Readonly<FormCardProps>) {
  const isExpired = form.expiresAt !== null && new Date(form.expiresAt) < new Date();
  const createdAtLabel = new Date(form.createdAt).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric'
  });

  return (
    <button
      type="button"
      onClick={onClick}
      className="w-full text-left bg-white rounded-2xl shadow-md p-6 transition-colors
        hover:bg-brand-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
    >
      <h3 className="text-lg font-bold text-gray-900 truncate">{form.title}</h3>
      <p className="mt-1 text-sm text-gray-500">Created {createdAtLabel}</p>
      <div className="mt-3 flex flex-wrap gap-2">
        <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${form.isPublic ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600'
          }`}>
          {form.isPublic ? 'Public' : 'Private'}
        </span>
        {form.expiresAt !== null && (
          <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${isExpired ? 'bg-red-100 text-red-700' : 'bg-brand-50 text-brand-700'
            }`}>
            {isExpired ? 'Expired' : `Expires ${new Date(form.expiresAt).toLocaleDateString()}`}
          </span>
        )}
      </div>
    </button>
  );
};
