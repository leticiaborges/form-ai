import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { Link, useNavigate } from "react-router-dom";
import { generateForm } from "../api/forms";
import z from "zod";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { getErrorMessage } from "../utils/getErrorMessage";
import { showSuccess, showError } from "../utils/toast";

const createFormSchema = z.object({
  title: z.string().max(255, "Title must be at most 255 characters").optional(),
  description: z.string().max(1024, "Description must be at most 1024 characters").optional(),
  sourceText: z.string().min(50, "Source text must be at least 50 characters long"),
  questionCount: z.coerce.number().int().min(1, "At least 1 question.").
    max(20, "At most 20 questions."),
  difficultyLevel: z.enum(["Easy", "Medium", "Hard"], "Difficulty level must be one of Easy, Medium, or Hard."),
  includeCorrectAnswers: z.boolean()
});

type CreateFormData = z.infer<typeof createFormSchema>;

export function CreateFormPage() {
  const navigate = useNavigate();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<CreateFormData>({
    resolver: zodResolver(createFormSchema) as any,
    defaultValues: {
      questionCount: 5,
      difficultyLevel: "Medium",
      includeCorrectAnswers: false
    }
  });

  async function onSubmit(data: CreateFormData) {
    try {
      const result = await generateForm(data);
      showSuccess('Form generated successfully.');
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
              Paste the content you want to turn into questions, and the AI will
              generate a draft form for you to edit.
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
                  'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
                  'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
                  (errors.title ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ')
                }
                placeholder="Leave blank to use a generated title"
                {...register('title')}
              />
              {errors.title && (
                <span className="text-xs text-red-500">{errors.title.message}</span>
              )}
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
                  'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
                  'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
                  (errors.description ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ')
                }
                placeholder="A short note about what this form is for…"
                {...register('description')}
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
                  'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
                  'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
                  (errors.sourceText ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ')
                }
                placeholder="Paste an article, study notes, or any text you want questions generated from…"
                {...register('sourceText')}
              />
              {errors.sourceText && (
                <span className="text-xs text-red-500">{errors.sourceText.message}</span>
              )}
            </div>

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
                    'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
                    'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
                    (errors.questionCount ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ')
                  }
                  {...register('questionCount')}
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
                  {...register('difficultyLevel')}
                >
                  <option value="Easy">Easy</option>
                  <option value="Medium">Medium</option>
                  <option value="Hard">Hard</option>
                </select>
              </div>
            </div>

            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input
                type="checkbox"
                className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                {...register('includeCorrectAnswers')}
              />
              Include correct answers (for graded forms)
            </label>

            <div className="flex items-center justify-between mt-2">
              <Link to="/dashboard" className="text-sm text-gray-500 hover:underline">
                Cancel
              </Link>
              <Button type="submit" isLoading={isSubmitting}>
                Generate form
              </Button>
            </div>
          </form>
        </div>
      </div>
    </BasePage>
  );
}