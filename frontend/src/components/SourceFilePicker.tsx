import { useState, type ChangeEvent } from "react";
import { ACCEPT_ATTRIBUTE, formatFileSize, validateSourceFile } from "../utils/sourceFile";

interface SourceFilePickerProps {
  id: string;
  file: File | null;
  onChange: (file: File | null) => void;
  disabled?: boolean;
  error?: string;
}

export function SourceFilePicker({
  id,
  file,
  onChange,
  disabled = false,
  error,
}: Readonly<SourceFilePickerProps>) {
  const [rejection, setRejection] = useState<string | null>(null);

  function handlePick(event: ChangeEvent<HTMLInputElement>) {
    const picked = event.target.files?.[0];
    event.target.value = "";

    if (!picked) return;

    const problem = validateSourceFile(picked);
    setRejection(problem);

    if (problem === null) onChange(picked);
  }

  function handleRemove() {
    setRejection(null);
    onChange(null);
  }

  const message = rejection ?? error;

  return (
    <div className="flex flex-col gap-1">
      <span className="text-sm font-medium text-gray-700">
        File <span className="text-gray-400 font-normal">(optional, one file)</span>
      </span>

      <div className="flex items-center gap-3">
        <label
          htmlFor={id}
          className={
            "inline-flex cursor-pointer items-center rounded-lg border border-brand-600 px-3 py-1 " +
            "text-sm font-semibold text-brand-600 hover:bg-brand-50 " +
            "has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-brand-500 " +
            (disabled ? "opacity-50 cursor-not-allowed pointer-events-none" : "")
          }
        >
          {file ? "Replace file" : "Attach a file"}
          <input
            id={id}
            type="file"
            accept={ACCEPT_ATTRIBUTE}
            className="sr-only"
            disabled={disabled}
            onChange={handlePick}
          />
        </label>

        {file && (
          <div className="flex min-w-0 items-center gap-2 text-sm text-gray-700">
            <span className="truncate" title={file.name}>
              {file.name}
            </span>
            <span className="shrink-0 text-gray-400">{formatFileSize(file.size)}</span>
            <button
              type="button"
              onClick={handleRemove}
              disabled={disabled}
              aria-label={`Remove ${file.name}`}
              className="shrink-0 text-gray-400 hover:text-red-500 disabled:opacity-50"
            >
              ✕
            </button>
          </div>
        )}
      </div>

      <span className="text-xs text-gray-400">
        PDF, Word (.docx), PowerPoint (.pptx) or text (.txt), up to 10 MB.
      </span>

      {message && (
        <span role="alert" className="text-xs text-red-500">
          {message}
        </span>
      )}
    </div>
  );
}
