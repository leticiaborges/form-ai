import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { Link, useNavigate } from "react-router-dom";
import { generateForm } from "../api/forms";
import z from "zod";
import { useController, useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { getErrorMessage } from "../utils/getErrorMessage";
import { showSuccess, showError } from "../utils/toast";
import { DateTimeInput } from "../components/DateTimeInput";
import { addDays, DATETIME_FORMATS, getDefaultFormatStringDateTime } from "../utils/dateUtils";
import { MAX_SOURCE_TEXT_LENGTH, MIN_SOURCE_TEXT_LENGTH } from "../utils/sourceFile";
import { SourceFilePicker } from "../components/SourceFilePicker";

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
    if (data.file) return;

    const pasted = data.sourceText.trim();

    if (pasted.length == 0) {
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
      showError(getErrorMessage(err, "Failed to generate form. Please try again."));
    }
  }

  return (
    <BasePage>
      <div className="flex-1 flex items-center justify-center px-4 py-8">
        <div className="w-full max-w-lg bg-white rounded-2xl shadow-md p-8">
          <div className="mb-6">
            <h3 className="text-xl font-semibold text-gray-900">Create a new form</h3>
            <p className="text-sm text-gray-500 mt-1">
              Paste the content you want to turn into questions, and the AI will generate a draft
              form for you to edit.
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
                className={
                  "w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none " +
                  "focus:ring-2 focus:ring-brand-500 focus:border-brand-500 " +
                  (errors.sourceText ? "border-red-400 focus:ring-red-400 " : "border-gray-300 ")
                }
                placeholder="Paste an article, study notes, or any text you want questions generated from…"
                {...register("sourceText")}
              />

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
