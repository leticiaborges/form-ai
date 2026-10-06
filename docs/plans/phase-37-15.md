# Slice 15: Frontend (file picker, checks, multipart, error messages, disclosure)

Covers FE-1 to FE-3, FE-5 to FE-7 and FE-9 of [`phase-37.md`](./phase-37.md) (FE-4's progress and step labels are replaced by one "Generating your form…" label). `CreateFormPage` gets a **separate file picker component**, client-side checks that mirror the backend, one message per error code, and a disclosure next to the Generate button. The backend contract (`POST /api/forms/generate`, multipart, optional `file`) is already built in slices 9 to 14, so this slice is frontend only.

This plan starts from the code as it is today: `generateForm` already sends `FormData` with an explicit `multipart/form-data` header, `MAX_SOURCE_TEXT_LENGTH = 100_000` is already in `CreateFormPage.tsx` (the backend's `FormSourceContent.MaxSourceTextLength` is also 100,000; the 30,000 in the phase spec is out of date and the code wins), and the page uses `react-hook-form` + `zod` 4.

## Decisions

| Topic | Decision |
|---|---|
| New files | `src/utils/sourceFile.ts` (limits and checks, one place), `src/components/SourceFilePicker.tsx` (the picker), `src/utils/generationErrors.ts` (code to message). Constants that used to live in the page move to `sourceFile.ts`. |
| Picker is dumb | `SourceFilePicker` holds no form state. It receives `file` and `onChange`, validates a picked file with `validateSourceFile`, and only calls `onChange(file)` when the file passes. A rejected pick shows its own message and leaves the previous file untouched. The page owns the value through `useController`. |
| One file | The `<input type="file">` has no `multiple`. Picking again replaces the file. |
| Client checks (FE-2) | Extension in `.pdf .docx .pptx .txt` (case-insensitive), 0 bytes rejected, over 10 MB rejected, pasted text at most 100,000 characters. Same numbers as the backend. Extension only: no magic bytes, as in the backend for PDFs. |
| At least one source (FE-3) | A `superRefine` on the schema. No file and blank text: "Paste some text or attach a file". No file and under 100 characters: "Use at least 100 characters, or attach a file". **With a file the 100-character minimum is not checked on the client**: for a PDF it doesn't apply (EP-4) and for `.txt/.docx/.pptx` the extracted length is only known to the server, which answers `SourceTextTooShort`. |
| Progress (FE-4) | **One label, no steps.** While the request runs the page shows "Generating your form…". The request is synchronous and the server reports no stage, so upload, reading and generating are not told apart: no upload percentage, no timer, no `onUploadProgress`. FE-4's step labels and upload progress are dropped on purpose. |
| Disabled while running (FE-5) | The Generate button already uses `isSubmitting` (react-hook-form keeps it true until `onSubmit` settles). The picker gets `disabled` from the same flag so the file can't be changed mid-request. |
| Errors (FE-6) | `getGenerateErrorMessage(err, fallback)`: a message per `code`; `413` (Kestrel's request limit) as too large; `429` uses the server's message (it carries one, with `code` null); anything else uses the server's message, then the fallback. `GenerationUnavailable` and `GenerationBudgetReached` both say "try again later". |
| Disclosure (FE-7) | One sentence under the buttons. **No terms or privacy page exists in the frontend** (nothing to update), so that half of FE-7 goes to `known-gaps.md`. FE-8 stands: nothing about storage. |
| Optional field | The text area keeps its label ("Source content", the existing tests find it by that text) and gets a helper line saying it is optional when a file is attached. |
| Playwright | Not in this slice. Slice 16 covers the browser flow (T-4). Vitest with MSW covers everything the component proves (FE-9). |

---

## 1. Shared limits and checks

### 1.1 New `frontend/src/utils/sourceFile.ts`

```ts
// The numbers mirror the backend: FormSourceContent.MaxSourceTextLength / MaxFileBytes and
// GenerateFormHandler.MinSourceTextLength. Change them there and here together.
export const MAX_SOURCE_TEXT_LENGTH = 100_000;
export const MIN_SOURCE_TEXT_LENGTH = 100;
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

export const ACCEPTED_EXTENSIONS = [".pdf", ".docx", ".pptx", ".txt"] as const;
export const ACCEPT_ATTRIBUTE = ACCEPTED_EXTENSIONS.join(",");

export function fileExtension(fileName: string): string {
  const dot = fileName.lastIndexOf(".");
  return dot === -1 ? "" : fileName.slice(dot).toLowerCase();
}

export function isPdf(file: File): boolean {
  return fileExtension(file.name) === ".pdf";
}

/** Returns why the file can't be used, or null when it can. */
export function validateSourceFile(file: File): string | null {
  const extension = fileExtension(file.name);

  if (!(ACCEPTED_EXTENSIONS as readonly string[]).includes(extension)) {
    return "That file type isn't supported. Use a PDF, Word (.docx), PowerPoint (.pptx) or text (.txt) file.";
  }
  if (file.size === 0) return "The file is empty.";
  if (file.size > MAX_FILE_BYTES) return "The file is too large. The maximum size is 10 MB.";

  return null;
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
```

### 1.2 New `frontend/src/utils/sourceFile.test.ts`

```ts
import { describe, expect, it } from "vitest";
import {
  MAX_FILE_BYTES,
  fileExtension,
  formatFileSize,
  isPdf,
  validateSourceFile,
} from "./sourceFile";

function fileOf(name: string, size: number) {
  return new File([new Uint8Array(size)], name);
}

describe("validateSourceFile", () => {
  it.each(["a.pdf", "a.docx", "a.pptx", "a.txt", "REPORT.PDF", "my.notes.TXT"])(
    "accepts %s",
    (name) => {
      expect(validateSourceFile(fileOf(name, 10))).toBeNull();
    },
  );

  it.each(["a.doc", "a.ppt", "a.png", "a.exe", "noextension"])("rejects %s", (name) => {
    expect(validateSourceFile(fileOf(name, 10))).toMatch(/isn't supported/);
  });

  it("rejects an empty file", () => {
    expect(validateSourceFile(fileOf("a.txt", 0))).toBe("The file is empty.");
  });

  it("accepts exactly 10 MB and rejects one byte more", () => {
    expect(validateSourceFile(fileOf("a.pdf", MAX_FILE_BYTES))).toBeNull();
    expect(validateSourceFile(fileOf("a.pdf", MAX_FILE_BYTES + 1))).toMatch(/too large/);
  });
});

describe("helpers", () => {
  it("reads the extension in lower case", () => {
    expect(fileExtension("A.B.PDF")).toBe(".pdf");
    expect(fileExtension("none")).toBe("");
  });

  it("recognises a PDF by extension", () => {
    expect(isPdf(fileOf("A.PDF", 1))).toBe(true);
    expect(isPdf(fileOf("a.txt", 1))).toBe(false);
  });

  it("formats sizes", () => {
    expect(formatFileSize(512)).toBe("512 B");
    expect(formatFileSize(2048)).toBe("2.0 KB");
    expect(formatFileSize(5 * 1024 * 1024)).toBe("5.0 MB");
  });
});
```

---

## 2. The picker component

### 2.1 New `frontend/src/components/SourceFilePicker.tsx`

The input is visually hidden (`sr-only`) and a `<label>` styled as a button opens it, so the control stays keyboard and screen-reader accessible. The input's value is cleared after every pick: without that, removing a file and picking the *same* file again would not fire `change`.

```tsx
import type { ChangeEvent } from "react";
import { useState } from "react";
import { ACCEPT_ATTRIBUTE, formatFileSize, validateSourceFile } from "../utils/sourceFile";

interface SourceFilePickerProps {
  id: string;
  file: File | null;
  onChange: (file: File | null) => void;
  disabled?: boolean;
  /** An error that comes from outside (the form's own validation). */
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
    // Lets the same file be picked again after it was removed.
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

      <span className="text-xs text-gray-400">PDF, Word (.docx), PowerPoint (.pptx) or text (.txt), up to 10 MB.</span>

      {message && (
        <span role="alert" className="text-xs text-red-500">
          {message}
        </span>
      )}
    </div>
  );
}
```

Notes:

- The input sits **inside** the label, so the label's text ("Attach a file" / "Replace file") is its accessible name and no separate `htmlFor` wiring is needed; `htmlFor` is kept so it also works if the markup is moved later.
- Tailwind's `has-[:focus-visible]` variant needs Tailwind 3.4+. If the project is older, drop those two classes; the focus ring is the only thing lost.

### 2.2 New `frontend/src/components/SourceFilePicker.test.tsx`

`userEvent.upload` filters files by the input's `accept` attribute by default, so the test that picks an unsupported type creates the user with `applyAccept: false` (the browser's file dialog filters, but a user can still drag a file or choose "All files").

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { SourceFilePicker } from "./SourceFilePicker";
import { MAX_FILE_BYTES } from "../utils/sourceFile";

function pdf(name = "report.pdf", size = 2048) {
  return new File([new Uint8Array(size)], name, { type: "application/pdf" });
}

function Harness({ onChange }: Readonly<{ onChange?: (f: File | null) => void }>) {
  const [file, setFile] = useState<File | null>(null);
  return (
    <SourceFilePicker
      id="file"
      file={file}
      onChange={(f) => {
        setFile(f);
        onChange?.(f);
      }}
    />
  );
}

describe("SourceFilePicker", () => {
  it("shows the name and size of the picked file", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("report.pdf", 2048));

    expect(screen.getByText("report.pdf")).toBeInTheDocument();
    expect(screen.getByText("2.0 KB")).toBeInTheDocument();
    expect(screen.getByText("Replace file")).toBeInTheDocument();
  });

  it("accepts one file only", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Attach a file") as HTMLInputElement;
    expect(input.multiple).toBe(false);
    expect(input.accept).toBe(".pdf,.docx,.pptx,.txt");
  });

  it("removes the file", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf());
    await user.click(screen.getByRole("button", { name: "Remove report.pdf" }));

    expect(screen.queryByText("report.pdf")).not.toBeInTheDocument();
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("can pick the same file again after removing it", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    const file = pdf();
    await user.upload(screen.getByLabelText("Attach a file"), file);
    await user.click(screen.getByRole("button", { name: "Remove report.pdf" }));
    await user.upload(screen.getByLabelText("Attach a file"), file);

    expect(screen.getByText("report.pdf")).toBeInTheDocument();
  });

  it("rejects an unsupported type and keeps the previous file", async () => {
    const user = userEvent.setup({ applyAccept: false });
    render(<Harness />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("keep.pdf"));
    await user.upload(
      screen.getByLabelText("Replace file"),
      new File(["x"], "photo.png", { type: "image/png" }),
    );

    expect(screen.getByRole("alert")).toHaveTextContent("isn't supported");
    expect(screen.getByText("keep.pdf")).toBeInTheDocument();
  });

  it("rejects a file over 10 MB", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("big.pdf", MAX_FILE_BYTES + 1));

    expect(screen.getByRole("alert")).toHaveTextContent("too large");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("clears the message once a valid file is picked", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("big.pdf", MAX_FILE_BYTES + 1));
    await user.upload(screen.getByLabelText("Attach a file"), pdf("ok.pdf"));

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("is locked while disabled", () => {
    render(
      <SourceFilePicker id="file" file={pdf()} onChange={vi.fn()} disabled />,
    );

    expect(screen.getByLabelText("Replace file")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Remove report.pdf" })).toBeDisabled();
  });

  it("shows an error that comes from outside", () => {
    render(<SourceFilePicker id="file" file={null} onChange={vi.fn()} error="Required" />);

    expect(screen.getByRole("alert")).toHaveTextContent("Required");
  });
});
```

---

## 3. Error messages

### 3.1 New `frontend/src/utils/generationErrors.ts`

```ts
import { isAxiosError } from "axios";
import type { MessageErrorResponse } from "../types/CustomResponse";
import { MAX_SOURCE_TEXT_LENGTH, MIN_SOURCE_TEXT_LENGTH } from "./sourceFile";

const TRY_LATER = "Form generation is unavailable right now. Please try again later.";
const TOO_LARGE = "The file is too large. The maximum size is 10 MB.";

// One message per code the generate endpoint can answer (ValidationErrorCode, AI-9).
const MESSAGES: Record<string, string> = {
  SourceFileUnsupported:
    "That file type isn't supported. Use a PDF, Word (.docx), PowerPoint (.pptx) or text (.txt) file.",
  SourceFileTooLarge: TOO_LARGE,
  SourceFileUnreadable:
    "We couldn't read that file. It may be damaged, password-protected or too long. Try another file.",
  SourceTextTooShort: `The text is too short to make questions from. Use at least ${MIN_SOURCE_TEXT_LENGTH} characters.`,
  SourceTextTooLong: `The text is too long. The maximum is ${MAX_SOURCE_TEXT_LENGTH.toLocaleString("en-US")} characters, pasted text and file together.`,
  GenerationOutputInvalid: "The AI returned a draft we couldn't use. Please try again.",
  GenerationUnavailable: TRY_LATER,
  GenerationBudgetReached: TRY_LATER,
};

export function getGenerateErrorMessage(err: unknown, fallback: string): string {
  if (!isAxiosError<MessageErrorResponse>(err)) return fallback;

  const code = err.response?.data?.code;
  if (code && MESSAGES[code]) return MESSAGES[code];

  // The request limit (about 11 MB) answers before the handler runs, with no code.
  if (err.response?.status === 413) return TOO_LARGE;

  // Everything else, a 429 included, carries its own message.
  return err.response?.data?.message ?? fallback;
}
```

### 3.2 New `frontend/src/utils/generationErrors.test.ts`

```ts
import { AxiosError, type AxiosResponse } from "axios";
import { describe, expect, it } from "vitest";
import { getGenerateErrorMessage } from "./generationErrors";

function failure(status: number, data: object) {
  return new AxiosError("failed", "ERR_BAD_REQUEST", undefined, undefined, {
    status,
    data,
  } as AxiosResponse);
}

describe("getGenerateErrorMessage", () => {
  it.each([
    ["SourceFileUnsupported", /isn't supported/],
    ["SourceFileTooLarge", /too large/],
    ["SourceFileUnreadable", /couldn't read that file/],
    ["SourceTextTooShort", /at least 100 characters/],
    ["SourceTextTooLong", /100,000 characters/],
    ["GenerationOutputInvalid", /couldn't use/],
    ["GenerationUnavailable", /try again later/],
    ["GenerationBudgetReached", /try again later/],
  ])("maps %s to its own message", (code, expected) => {
    expect(getGenerateErrorMessage(failure(400, { message: "server text", code }), "fallback")).toMatch(
      expected,
    );
  });

  it("explains a 413 from the request size limit", () => {
    expect(getGenerateErrorMessage(failure(413, {}), "fallback")).toMatch(/too large/);
  });

  it("uses the server message for a 429", () => {
    const err = failure(429, { message: "Too many forms. Try later.", code: null });
    expect(getGenerateErrorMessage(err, "fallback")).toBe("Too many forms. Try later.");
  });

  it("uses the server message for an unknown code", () => {
    const err = failure(400, { message: "Title is too long.", code: null });
    expect(getGenerateErrorMessage(err, "fallback")).toBe("Title is too long.");
  });

  it("falls back when there is no response or the error isn't an axios one", () => {
    expect(getGenerateErrorMessage(new Error("boom"), "fallback")).toBe("fallback");
    expect(getGenerateErrorMessage(new AxiosError("network"), "fallback")).toBe("fallback");
  });
});
```

---

## 4. API client

### 4.1 `frontend/src/api/forms.ts`

Replace `GenerateFormPayload` and `generateForm`:

```ts
export interface GenerateFormPayload {
  title?: string;
  description?: string;
  sourceText: string;
  /** At most one: .pdf, .docx, .pptx or .txt. */
  file?: File | null;
  questionCount: number;
  difficultyLevel: string;
  isGraded: boolean;
  showResultsAfterSubmit: boolean;
  expiresAt: Date;
}
```

```ts
export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
  const body = new FormData();

  body.append("title", payload.title?.trim() || "");
  body.append("description", payload.description?.trim() || "");
  // Left out when blank: a file alone is a valid request.
  if (payload.sourceText.trim()) body.append("sourceText", payload.sourceText);
  if (payload.file) body.append("file", payload.file);
  body.append("questionCount", String(payload.questionCount));
  body.append("difficultyLevel", payload.difficultyLevel);
  body.append("isGraded", String(payload.isGraded));
  body.append("showResultsAfterSubmit", String(payload.showResultsAfterSubmit));
  body.append("expiresAt", payload.expiresAt.toISOString());

  // Set explicitly: the axios default is JSON, which would serialize the FormData.
  const response = await api.post<GenerateFormResult>("/forms/generate", body, {
    headers: {
      "Content-Type": "multipart/form-data",
    },
  });

  return response.data;
}
```

The `file` field name matches `GenerateFormDataRequest.File` (the e2e specs use `file` too). Nothing else in the file changes.

---

## 5. The page

### 5.1 `frontend/src/pages/CreateFormPage.tsx`

Replace the whole file. What changed, to diff by eye: imports; the constants moved to `sourceFile.ts`; the schema gets `file` and a `superRefine`; `defaultValues` gets `sourceText: ""` and `file: null`; the picker, a helper line under the text area, a status line shown while submitting and the disclosure are new JSX. Everything else is as it was.

```tsx
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { SourceFilePicker } from "../components/SourceFilePicker";
import { Link, useNavigate } from "react-router-dom";
import { generateForm } from "../api/forms";
import z from "zod";
import { useController, useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { getGenerateErrorMessage } from "../utils/generationErrors";
import { showSuccess, showError } from "../utils/toast";
import { DateTimeInput } from "../components/DateTimeInput";
import { addDays, DATETIME_FORMATS, getDefaultFormatStringDateTime } from "../utils/dateUtils";
import { MAX_SOURCE_TEXT_LENGTH, MIN_SOURCE_TEXT_LENGTH } from "../utils/sourceFile";

const createFormSchema = z
  .object({
    title: z.string().max(255, "Title must be at most 255 characters").optional(),
    description: z.string().max(1024, "Description must be at most 1024 characters").optional(),
    sourceText: z
      .string()
      .max(
        MAX_SOURCE_TEXT_LENGTH,
        `Source text must be at most ${MAX_SOURCE_TEXT_LENGTH} characters long`,
      ),
    file: z.instanceof(File).nullable(),
    questionCount: z.coerce
      .number()
      .int()
      .min(1, "At least 1 question.")
      .max(20, "At most 20 questions."),
    difficultyLevel: z.enum(
      ["Easy", "Medium", "Hard"],
      "Difficulty level must be one of Easy, Medium, or Hard.",
    ),
    isGraded: z.boolean(),
    showResultsAfterSubmit: z.boolean(),
    expiresAt: z
      .string()
      .min(1, "Pick an expiry date and time")
      .refine((v) => !Number.isNaN(Date.parse(v)), "Enter a valid date and time.")
      .refine((v) => new Date(v) > new Date(), "The expiry must be in the future."),
  })
  .superRefine((data, ctx) => {
    // With a file, the minimum is the server's call: it doesn't apply to a PDF, and for the other
    // formats only the server knows how much text the file holds.
    if (data.file) return;

    const pasted = data.sourceText.trim();

    if (pasted.length === 0) {
      ctx.addIssue({
        code: "custom",
        path: ["sourceText"],
        message: "Paste some text or attach a file to generate a form.",
      });
    } else if (pasted.length < MIN_SOURCE_TEXT_LENGTH) {
      ctx.addIssue({
        code: "custom",
        path: ["sourceText"],
        message: `Use at least ${MIN_SOURCE_TEXT_LENGTH} characters, or attach a file.`,
      });
    }
  });

type CreateFormInput = z.input<typeof createFormSchema>;
type CreateFormData = z.output<typeof createFormSchema>;

export function CreateFormPage() {
  const navigate = useNavigate();
  const currentDate = new Date();
  currentDate.setHours(23, 59, 59, 999);
  const defaultDate = addDays(currentDate, 7);

  const {
    register,
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateFormInput, unknown, CreateFormData>({
    resolver: zodResolver(createFormSchema),
    defaultValues: {
      sourceText: "",
      file: null,
      questionCount: 5,
      difficultyLevel: "Medium",
      isGraded: false,
      showResultsAfterSubmit: false,
      expiresAt: getDefaultFormatStringDateTime(defaultDate, DATETIME_FORMATS.DATETIME_HHMM),
    },
  });

  const { field, fieldState } = useController({ name: "expiresAt", control });
  const { field: fileField, fieldState: fileState } = useController({ name: "file", control });
  const isGraded = useWatch({ control, name: "isGraded" });

  async function onSubmit(data: CreateFormData) {
    try {
      const result = await generateForm({
        ...data,
        // The checkbox is hidden, not cleared, when "Graded form" is unticked, so its stale
        // value must not travel with an ungraded form.
        showResultsAfterSubmit: data.isGraded && data.showResultsAfterSubmit,
        expiresAt: new Date(data.expiresAt),
      });
      showSuccess("Form generated successfully.");
      navigate(`/forms/${result.formId}/edit`);
    } catch (err: unknown) {
      showError(getGenerateErrorMessage(err, "Failed to generate form. Please try again."));
    }
  }

  return (
    <BasePage>
      <div className="flex-1 flex items-center justify-center px-4 py-8">
        <div className="w-full max-w-lg bg-white rounded-2xl shadow-md p-8">
          <div className="mb-6">
            <h3 className="text-xl font-semibold text-gray-900">Create a new form</h3>
            <p className="text-sm text-gray-500 mt-1">
              Paste the content you want to turn into questions, attach a file, or both, and the AI
              will generate a draft form for you to edit.
            </p>
          </div>

          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <label htmlFor="title" className="text-sm font-medium text-gray-700">
                Form title <span className="text-gray-400 font-normal">(optional)</span>
              </label>
              <input
                id="title"
                type="text"
                maxLength={255}
                className={
                  "w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none " +
                  "focus:ring-2 focus:ring-brand-500 focus:border-brand-500 " +
                  (errors.title ? "border-red-400 focus:ring-red-400 " : "border-gray-300 ")
                }
                placeholder="Leave blank to use a generated title"
                {...register("title")}
              />
              {errors.title && <span className="text-xs text-red-500">{errors.title.message}</span>}
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="description" className="text-sm font-medium text-gray-700">
                Description <span className="text-gray-400 font-normal">(optional)</span>
              </label>
              <textarea
                id="description"
                rows={2}
                maxLength={1024}
                className={
                  "w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none " +
                  "focus:ring-2 focus:ring-brand-500 focus:border-brand-500 " +
                  (errors.description ? "border-red-400 focus:ring-red-400 " : "border-gray-300 ")
                }
                placeholder="A short note about what this form is for…"
                {...register("description")}
              />
              {errors.description && (
                <span className="text-xs text-red-500">{errors.description.message}</span>
              )}
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="sourceText" className="text-sm font-medium text-gray-700">
                Source content
              </label>
              <textarea
                id="sourceText"
                rows={8}
                aria-describedby="sourceTextHint"
                className={
                  "w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none " +
                  "focus:ring-2 focus:ring-brand-500 focus:border-brand-500 " +
                  (errors.sourceText ? "border-red-400 focus:ring-red-400 " : "border-gray-300 ")
                }
                placeholder="Paste an article, study notes, or any text you want questions generated from…"
                {...register("sourceText")}
              />
              <span id="sourceTextHint" className="text-xs text-gray-400">
                Optional if you attach a file. With a file, this text is used together with it.
              </span>
              {errors.sourceText && (
                <span className="text-xs text-red-500">{errors.sourceText.message}</span>
              )}
            </div>

            <SourceFilePicker
              id="sourceFile"
              file={fileField.value ?? null}
              onChange={fileField.onChange}
              disabled={isSubmitting}
              error={fileState.error?.message}
            />

            <div className="grid grid-cols-2 gap-4">
              <div className="flex flex-col gap-1">
                <label htmlFor="questionCount" className="text-sm font-medium text-gray-700">
                  Number of questions
                </label>
                <input
                  id="questionCount"
                  type="number"
                  min={1}
                  max={20}
                  className={
                    "w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none " +
                    "focus:ring-2 focus:ring-brand-500 focus:border-brand-500 " +
                    (errors.questionCount
                      ? "border-red-400 focus:ring-red-400 "
                      : "border-gray-300 ")
                  }
                  {...register("questionCount")}
                />
                {errors.questionCount && (
                  <span className="text-xs text-red-500">{errors.questionCount.message}</span>
                )}
              </div>

              <div className="flex flex-col gap-1">
                <label htmlFor="difficultyLevel" className="text-sm font-medium text-gray-700">
                  Difficulty
                </label>
                <select
                  id="difficultyLevel"
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                  focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
                  {...register("difficultyLevel")}
                >
                  <option value="Easy">Easy</option>
                  <option value="Medium">Medium</option>
                  <option value="Hard">Hard</option>
                </select>
              </div>
            </div>

            <div className="flex items-center gap-4 text-sm text-gray-700">
              <DateTimeInput
                label="Expires at"
                id="expiresAt"
                value={field.value ?? ""}
                onChange={field.onChange}
                onBlur={field.onBlur}
                error={fieldState.error?.message}
                timeFormat="HH:mm"
              ></DateTimeInput>
            </div>

            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                {...register("isGraded")}
              />
              {/**/}
              Graded form
            </label>

            {isGraded && (
              <label className="flex items-center gap-2 text-sm text-gray-700">
                <input
                  type="checkbox"
                  className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                  {...register("showResultsAfterSubmit")}
                />
                Show score after submit
              </label>
            )}

            <div className="flex items-center justify-between mt-2">
              <Link to="/dashboard" className="text-sm text-gray-500 hover:underline">
                Cancel
              </Link>
              <Button type="submit" isLoading={isSubmitting}>
                Generate form
              </Button>
            </div>

            <p role="status" aria-live="polite" className="min-h-5 text-sm text-gray-600">
              {isSubmitting ? "Generating your form…" : ""}
            </p>

            <p className="text-xs text-gray-400">
              The text and files you add are sent to an AI provider to generate the form.
            </p>
          </form>
        </div>
      </div>
    </BasePage>
  );
}
```

Things to watch:

- A `role="status"` paragraph with no text is invisible but keeps its height (`min-h-5`), so the layout doesn't jump when the label appears.
- `Button` shows "Loading…" while submitting, so the line under it is what says what is happening. `Button` is shared and stays untouched.
- `z.instanceof(File)` needs `File` at module load. It exists in browsers and in the jsdom test environment.
- If TypeScript complains that `file` is missing in `CreateFormInput` somewhere else, nothing else uses this type.

---

## 6. Page tests

### 6.1 `frontend/src/pages/CreateFormPage.test.tsx`

Keep the existing tests. Add the imports and mock at the top, then the new `describe` blocks at the end of the file.

Top of the file (the first import line gains `within`-free additions; add `vi` and the mock):

```tsx
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateFormPage } from "./CreateFormPage";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";
import { showError } from "../utils/toast";
import { MAX_FILE_BYTES } from "../utils/sourceFile";

// The page reports failures through a toast, and the tests render no toaster.
vi.mock("../utils/toast", () => ({
  showSuccess: vi.fn(),
  showError: vi.fn(),
  showWarning: vi.fn(),
}));
```

Helpers, next to `scoreCheckbox`:

```tsx
const GENERATE_OK = () => HttpResponse.json({ formId: "form-1", title: "Generated" }, { status: 201 });

function pdf(name = "report.pdf", size = 2048) {
  return new File([new Uint8Array(size)], name, { type: "application/pdf" });
}

function attach(user: ReturnType<typeof userEvent.setup>, file: File) {
  return user.upload(screen.getByLabelText("Attach a file"), file);
}

function generateButton() {
  return screen.getByRole("button", { name: "Generate form" });
}
```

Add `beforeEach(() => vi.mocked(showError).mockClear());` inside the existing top-level `describe("CreateFormPage", ...)`, or at the top of each new block.

New blocks:

```tsx
describe("CreateFormPage: source", () => {
  beforeEach(() => vi.mocked(showError).mockClear());

  it("asks for a source when there is neither text nor a file, and sends nothing", async () => {
    let called = false;
    server.use(http.post(GENERATE_URL, () => ((called = true), GENERATE_OK())));

    const user = userEvent.setup();
    renderCreate();

    await user.click(generateButton());

    expect(await screen.findByText(/Paste some text or attach a file/)).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it("needs 100 characters of text when there is no file", async () => {
    let called = false;
    server.use(http.post(GENERATE_URL, () => ((called = true), GENERATE_OK())));

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), "too short");
    await user.click(generateButton());

    expect(await screen.findByText(/Use at least 100 characters, or attach a file/)).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it("accepts a file alone and sends it, without a sourceText field", async () => {
    let body: FormData | undefined;
    server.use(
      http.post(GENERATE_URL, async ({ request }) => {
        body = await request.formData();
        return GENERATE_OK();
      }),
    );

    const user = userEvent.setup();
    renderCreate();

    await attach(user, pdf("report.pdf"));
    await user.click(generateButton());

    await waitFor(() => expect(body).toBeDefined());
    expect((body?.get("file") as File).name).toBe("report.pdf");
    expect(body?.has("sourceText")).toBe(false);
  });

  it("accepts short pasted text next to a file and sends both", async () => {
    let body: FormData | undefined;
    server.use(
      http.post(GENERATE_URL, async ({ request }) => {
        body = await request.formData();
        return GENERATE_OK();
      }),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), "Focus on chapter 2.");
    await attach(user, pdf());
    await user.click(generateButton());

    await waitFor(() => expect(body).toBeDefined());
    expect(body?.get("sourceText")).toBe("Focus on chapter 2.");
    expect(body?.get("file")).toBeInstanceOf(File);
  });

  it("does not send a file that was removed", async () => {
    let body: FormData | undefined;
    server.use(
      http.post(GENERATE_URL, async ({ request }) => {
        body = await request.formData();
        return GENERATE_OK();
      }),
    );

    const user = userEvent.setup();
    renderCreate();

    await attach(user, pdf("report.pdf"));
    await user.click(screen.getByRole("button", { name: "Remove report.pdf" }));
    await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
    await user.click(generateButton());

    await waitFor(() => expect(body).toBeDefined());
    expect(body?.has("file")).toBe(false);
  });

  it("rejects a file over 10 MB before any request", async () => {
    let called = false;
    server.use(http.post(GENERATE_URL, () => ((called = true), GENERATE_OK())));

    const user = userEvent.setup();
    renderCreate();

    await attach(user, pdf("big.pdf", MAX_FILE_BYTES + 1));
    await user.click(generateButton());

    expect(await screen.findByText(/too large/)).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it("shows the disclosure next to the Generate button", () => {
    renderCreate();

    expect(screen.getByText(/sent to an AI provider/)).toBeInTheDocument();
  });
});

describe("CreateFormPage: while generating", () => {
  it("disables the button and the picker until the request finishes, and says it is working", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    server.use(
      http.post(GENERATE_URL, async () => {
        await gate;
        return GENERATE_OK();
      }),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
    await user.click(generateButton());

    // The button is replaced by its "Loading…" state, and can't be clicked twice.
    expect(await screen.findByRole("button", { name: "Loading…" })).toBeDisabled();
    expect(screen.getByLabelText("Attach a file")).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent("Generating your form");

    release();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Loading…" })).not.toBeInTheDocument());
  });

  it("sends one request even if the button is clicked again while waiting", async () => {
    let calls = 0;
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    server.use(
      http.post(GENERATE_URL, async () => {
        calls++;
        await gate;
        return GENERATE_OK();
      }),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
    await user.click(generateButton());
    await screen.findByRole("button", { name: "Loading…" });
    await user.click(screen.getByRole("button", { name: "Loading…" }));

    release();
    await waitFor(() => expect(calls).toBe(1));
  });
});

describe("CreateFormPage: errors", () => {
  beforeEach(() => vi.mocked(showError).mockClear());

  it.each([
    ["SourceFileUnsupported", 400, /isn't supported/],
    ["SourceFileTooLarge", 400, /too large/],
    ["SourceFileUnreadable", 400, /couldn't read that file/],
    ["SourceTextTooShort", 400, /at least 100 characters/],
    ["SourceTextTooLong", 400, /100,000 characters/],
    ["GenerationOutputInvalid", 502, /couldn't use/],
    ["GenerationUnavailable", 503, /try again later/],
    ["GenerationBudgetReached", 503, /try again later/],
  ])("shows a specific message for %s", async (code, status, expected) => {
    server.use(
      http.post(GENERATE_URL, () =>
        HttpResponse.json({ message: "server text", errors: null, code }, { status }),
      ),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
    await user.click(generateButton());

    await waitFor(() => expect(showError).toHaveBeenCalledTimes(1));
    expect(vi.mocked(showError).mock.calls[0][0]).toMatch(expected);
  });

  it("uses the server's message for a 429", async () => {
    server.use(
      http.post(GENERATE_URL, () =>
        HttpResponse.json({ message: "Too many requests.", errors: null, code: null }, { status: 429 }),
      ),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
    await user.click(generateButton());

    await waitFor(() => expect(showError).toHaveBeenCalledWith("Too many requests."));
  });

  it("lets the user try again after a failure", async () => {
    server.use(
      http.post(GENERATE_URL, () =>
        HttpResponse.json({ message: "x", errors: null, code: "GenerationUnavailable" }, { status: 503 }),
      ),
    );

    const user = userEvent.setup();
    renderCreate();

    await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
    await user.click(generateButton());
    await waitFor(() => expect(showError).toHaveBeenCalled());

    expect(generateButton()).toBeEnabled();
    expect(screen.getByLabelText("Attach a file")).toBeEnabled();
  });
});
```

Notes on the tests:

- The existing `it.each` for `showResultsAfterSubmit` types `SOURCE_TEXT` (72 characters) and still passes **only because** a text-only request needs 100 characters now: **it has 72**. Change `SOURCE_TEXT` to a longer string:

  ```tsx
  const SOURCE_TEXT =
    "The quick brown fox jumps over the lazy dog, again and again and again. " +
    "It never gets tired, and the dog never wakes up, no matter how many times it happens.";
  ```

  (about 160 characters). Without this the existing four tests fail on the client minimum, which is the new rule working.
- A MSW handler written as `() => ((called = true), GENERATE_OK())` is a comma expression: it records the call and returns the response. Write it as a block if you prefer.
- `user.upload` needs the input enabled, which is why the disabled assertion happens while the request is held.

---

## 7. Docs

- **`docs/known-gaps.md`**
  - Edit the row *Generation from PDF, Word, image or URL*: the endpoint is `POST /api/forms/generate` and accepts `.txt`, `.docx`, `.pptx` and `.pdf`, and the frontend now has a picker. Images and URLs are still not built (`PdfExtractor`/`UrlScraper` may already be gone; check the row against the code).
  - New row, **No terms or privacy page**: the Generate button has a one-line disclosure that text and files go to an AI provider (FE-7), but the frontend has no terms or privacy page to update, and nothing tells the user what is stored (FE-8, decision: docs later).
  - New row, **No upload progress or stages**: the request is synchronous and the server reports no stage, so the page shows one "Generating your form…" label for upload, reading and generating together.
  - New row, **Client checks stop at the extension**: type is checked by extension and size only, as on the backend for PDFs, and the 100-character minimum for `.txt/.docx/.pptx` is only known after extraction, so it is the server that answers `SourceTextTooShort`.
- **`CLAUDE.md`**
  - In the *generate* bullet under "Forms and questions", replace the last sentence `No file field exists yet.` with: the frontend sends `file` as one optional multipart field (the picker is `SourceFilePicker`, limits in `frontend/src/utils/sourceFile.ts`, mirrored from the backend); a blank `sourceText` is left out of the request. Add that error codes map to messages in `frontend/src/utils/generationErrors.ts`, and that the client skips the 100-character minimum whenever a file is attached.
- **`docs/adr/0009-source-content-policy.md`**: in *Consequences*, the sentence about the disclosure can now say it exists ("The Generate button says files are sent to an AI provider"). No new ADR: nothing here is hard to reverse.
- **`docs/plans/phase-37.md`**: mark slice 15 done (the closing line "Slices 1–5 are done" can list the finished slices).

---

## 8. Verify

```bash
cd frontend
npm run lint
npx tsc -b
npm test -- sourceFile SourceFilePicker generationErrors CreateFormPage
npm test
```

Manually, with the API and Vite running (`dotnet run --project src/FormAI.API`, `npm run dev`), signed in, against the fake or the real gateway:

1. **Text only** still works end to end, and shows "Generating your form…" under the button.
2. **A PDF alone**: pick it, name and size appear, Generate, land on the draft.
3. **Each rejection**: a `.png`, a `.doc`, a 0-byte file, an 11 MB file. Each shows its message under the picker and does not replace a file already picked. Remove the file and pick the same one again.
4. **While waiting**: throttle the network in the browser dev tools (Slow 3G) and upload a 5 to 10 MB PDF. "Generating your form…" shows the whole time. The Generate button and the picker stay disabled throughout and come back after the result or an error.
5. **Error codes** with the fake gateway markers in the pasted text: `[fake:down]` (try again later), `[fake:truncated]` and `[fake:invalid]` (the draft could not be used), a text file under 100 characters (too short), a `.txt` renamed `x.docx` (could not read), and a PDF alongside `[fake:pdf-rejected]` (could not read).
6. **Too many requests**: eleven generations in an hour show the server's 429 message.
7. **Pasted text and a file together** reach the server as two fields (network tab: `sourceText` and `file` in the multipart body).

## Done when

- `SourceFilePicker` is its own component: one file, `.pdf .docx .pptx .txt`, name and size, a remove action, and its own message for a rejected pick.
- The page rejects, before any request: no source, under 100 characters without a file, a file of the wrong type, empty or over 10 MB, and pasted text over 100,000 characters.
- The request is multipart with `file` and, when not blank, `sourceText`; one "Generating your form…" label shows while it runs; the button and the picker stay disabled until the request ends, and a failure re-enables them.
- Each error code, a 413 and a 429 show their own message; `GenerationUnavailable` and `GenerationBudgetReached` say "try again later".
- The disclosure shows next to the Generate button.
- Vitest passes for the helpers, the picker, the messages and the page; docs and `known-gaps.md` are updated.

## Suggested commits

1. `feat(frontend): share the source file limits and checks`  (`sourceFile.ts` and its tests)
2. `feat(frontend): add a file picker component`  (`SourceFilePicker` and its tests)
3. `feat(frontend): map each generation error code to a message`  (`generationErrors.ts` and its tests)
4. `feat(frontend): attach a file on the create form page`  (`forms.ts`, `CreateFormPage`, page tests)
5. `docs: record the file picker in CLAUDE.md and known-gaps`
