interface ResultsTabProps {
  formId: string;
}

export function ResultsTab({ formId }: Readonly<ResultsTabProps>) {
  return (
    <div className="py-16 text-center text-gray-400">
      <p className="text-lg">Results are coming in phase 19.</p>
      <p className="mt-1 text-sm">Form {formId}</p>
    </div>
  );
}