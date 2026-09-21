import type { AnswerPayload, AnswerQuestion, QuestionGradingInfo } from "../../types/submission";
import { normalizeOptionText } from "../../utils/textMatch";

interface AnswerQuestionCardProps {
  question: AnswerQuestion;
  index: number;
  answer?: AnswerPayload;
  error?: string;
  readOnly?: boolean;
  grading?: QuestionGradingInfo;
  onSingleChange?: (optionId: string) => void;
  onMultipleToggle?: (optionId: string) => void;
  onTextChange?: (value: string) => void;
  onNumericChange?: (value: string) => void;
}

function isOptionSelected(
  question: AnswerQuestion,
  optionId: string,
  optionText: string,
  answer: AnswerPayload | undefined,
  readOnly?: boolean,
): boolean {
  if (readOnly) {
    const selectedTexts = answer?.selectedOptionTexts ?? [];
    return selectedTexts.some((t) => normalizeOptionText(t) === normalizeOptionText(optionText));
  }

  return question.type === "Single"
    ? answer?.selectedOptionIds?.[0] === optionId
    : (answer?.selectedOptionIds?.includes(optionId) ?? false);
}

function optionTintClass(
  selected: boolean,
  optionText: string,
  grading?: QuestionGradingInfo,
): string {
  if (!grading || !selected) return "";

  const isCorrect = grading.correctOptionTexts.some(
    (t) => normalizeOptionText(t) === normalizeOptionText(optionText),
  );

  const color = isCorrect ? "green" : "red";
  return `bg-${color}-50 border border-${color}-200 rounded-lg px-2 py-1 -mx-2`;
}

function CorrectAnswerBox({
  question,
  grading,
}: Readonly<{ question: AnswerQuestion; grading: QuestionGradingInfo }>) {
  if (!grading.hasAnswerKey) {
    return (
      <div className="mt-3 rounded-lg bg-gray-50 p-3 text-xs text-gray-400 italic">
        No correct answer defined
      </div>
    );
  }

  return (
    <div className="mt-3 rounded-lg bg-gray-50 p-3 text-xs">
      <p className="mb-1 font-medium text-gray-500">Correct answer</p>
      {question.type === "Single" && (
        <p className="text-gray-700">{grading.correctOptionTexts[0]}</p>
      )}
      {question.type === "Multiple" && (
        <ul className="list-inside list-disc text-gray-700">
          {grading.correctOptionTexts.map((text) => (
            <li key={text}>{text}</li>
          ))}
        </ul>
      )}
      {(question.type === "Text" || question.type === "Numeric") && (
        <p className="text-gray-700">{grading.correctAnswerText}</p>
      )}
    </div>
  );
}

export function QuestionAnswerCard({
  question,
  index,
  answer,
  error,
  readOnly,
  grading,
  onSingleChange,
  onMultipleToggle,
  onTextChange,
  onNumericChange,
}: Readonly<AnswerQuestionCardProps>) {
  return (
    <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <p className="text-question font-medium text-gray-900">
          {index + 1}. {question.text}
          {question.isRequired && <span className="text-red-500"> *</span>}
        </p>

        {readOnly && grading && (
          <div className="flex shrink-0 items-center gap-2">
            <span className="text-xs font-medium text-gray-600">
              {grading.earnedScore}/{grading.points}
            </span>
            {grading.isCorrect ? (
              <svg
                className="h-5 w-5 text-green-600"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
              </svg>
            ) : (
              <svg
                className="h-5 w-5 text-red-600"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            )}
          </div>
        )}
      </div>

      <div className="mt-3 flex flex-col gap-2">
        {question.type === "Single" &&
          question.options.map((opt) => {
            const selected = isOptionSelected(question, opt.id, opt.text, answer, readOnly);
            return (
              <label
                key={opt.id}
                className={`flex items-center gap-2 text-option text-gray-700 ${optionTintClass(selected, opt.text, grading)}`}
              >
                <input
                  type="radio"
                  name={question.id}
                  checked={selected}
                  disabled={readOnly}
                  onChange={() => onSingleChange?.(opt.id)}
                  className="text-brand-600 focus:ring-brand-500"
                />
                {opt.text}
              </label>
            );
          })}

        {question.type === "Multiple" &&
          question.options.map((opt) => {
            const selected = isOptionSelected(question, opt.id, opt.text, answer, readOnly);
            return (
              <label
                key={opt.id}
                className={`flex items-center gap-2 text-option text-gray-700 ${optionTintClass(selected, opt.text, grading)}`}
              >
                <input
                  type="checkbox"
                  checked={selected}
                  disabled={readOnly}
                  onChange={() => onMultipleToggle?.(opt.id)}
                  className="rounded text-brand-600 focus:ring-brand-500"
                />
                {opt.text}
              </label>
            );
          })}

        {question.type === "Text" && (
          <textarea
            rows={3}
            value={answer?.textValue ?? ""}
            disabled={readOnly}
            onChange={(e) => onTextChange?.(e.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                                   focus:ring-2 focus:ring-brand-500 focus:border-brand-500 disabled:bg-gray-50"
          />
        )}

        {question.type === "Numeric" && (
          <input
            type="number"
            value={answer?.numericValue ?? ""}
            disabled={readOnly}
            onChange={(e) => onNumericChange?.(e.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                                   focus:ring-2 focus:ring-brand-500 focus:border-brand-500 disabled:bg-gray-50"
          />
        )}
      </div>

      {error && <p className="mt-2 text-xs text-red-500">{error}</p>}

      {readOnly && grading && <CorrectAnswerBox question={question} grading={grading} />}
    </div>
  );
}
