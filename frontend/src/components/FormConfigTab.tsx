import { DateTimeInput } from "./DateTimeInput";

interface FormConfigTabProps {
  expiresAt: string;
  onExpiresAtChange: (value: string) => void;
  expiresAtError: string;
  isGraded: boolean;
  showResultsAfterSubmit: boolean;
  onShowResultsAfterSubmitChange: (value: boolean) => void;
}

export function FormConfigTab({
  expiresAt,
  onExpiresAtChange,
  expiresAtError,
  isGraded,
  showResultsAfterSubmit,
  onShowResultsAfterSubmitChange,
}: Readonly<FormConfigTabProps>) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <DateTimeInput
          id="expiresAt"
          label="Expires at"
          value={expiresAt}
          onChange={onExpiresAtChange}
          error={expiresAtError}
          timeFormat="HH:mm"
        />
      </div>

      {isGraded && (
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={showResultsAfterSubmit}
            onChange={(e) => onShowResultsAfterSubmitChange(e.target.checked)}
            className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
          />
          Show score after submit
        </label>
      )}
    </div>
  );
}
