import { useParams } from "react-router-dom";
import type {
  AnswerForm,
  AnswerPayload,
  AnswerQuestion,
  SubmitFormResult,
} from "../types/submission";
import { useEffect, useState } from "react";
import { getRespondentToken } from "../utils/respondentToken";
import { getFormToAnswer, getMySubmission, submitForm } from "../api/submissions";
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { QuestionAnswerCard } from "../components/respond/QuestionAnswerCard";
import { getErrorMessage } from "../utils/getErrorMessage";
import { isNumericInRange, NUMERIC_RANGE_MESSAGE } from "../utils/numericRange";
import { showError } from "../utils/toast";
import type { CustomResponse } from "../types/CustomResponse";

type PageState =
  "loading" | "alreadySubmitted" | "expired" | "ready" | "submitting" | "submitted" | "error";

function isAnswered(question: AnswerQuestion, answer?: AnswerPayload): boolean {
  if (!answer) return false;

  switch (question.type) {
    case "Single":
    case "Multiple":
      return (answer.selectedOptionIds?.length ?? 0) > 0;
    case "Text":
      return !!answer.textValue?.trim();
    case "Numeric":
      return answer.numericValue !== undefined;
  }
}

export function FormAnswerPage() {
  const { id } = useParams<{ id: string }>();
  const [respondentToken] = useState(getRespondentToken());

  const [state, setState] = useState<PageState>("loading");
  const [form, setForm] = useState<AnswerForm | null>(null);

  const [answers, setAnswers] = useState<Record<string, AnswerPayload>>({});
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const [expiredMessage, setExpiredMessage] = useState<string | null>(null);
  const [result, setResult] = useState<SubmitFormResult | null>(null);

  useEffect(() => {
    if (!id) return;

    getMySubmission(id, respondentToken)
      .then((sub) => {
        if (sub.hasSubmitted) {
          setState("alreadySubmitted");
          return;
        }

        return getFormToAnswer(id).then((data) => {
          setForm(data);
          setState("ready");
        });
      })
      .catch((err) => {
        const e = err as CustomResponse;
        if (e.response?.data?.code === "FormExpired") {
          setExpiredMessage(getErrorMessage(err));
          setState("expired");
          return;
        }

        setState("error");
      });
  }, [id, respondentToken]);

  function setSingleAnswer(questionId: string, optionId: string) {
    setAnswers((a) => ({
      ...a,
      [questionId]: {
        selectedOptionIds: [optionId],
      },
    }));
  }

  function toggleMultipleAnswer(questionId: string, optionId: string) {
    setAnswers((a) => {
      const current = a[questionId]?.selectedOptionIds ?? [];
      const next = current.includes(optionId)
        ? current.filter((o) => o !== optionId)
        : [...current, optionId];

      return { ...a, [questionId]: { selectedOptionIds: next } };
    });
  }

  function setTextAnswer(questionId: string, value: string) {
    setAnswers((a) => ({ ...a, [questionId]: { textValue: value } }));
  }

  function setNumericAnswer(questionId: string, value: string) {
    setAnswers((a) => ({
      ...a,
      [questionId]: {
        numericValue: value === "" ? undefined : Number(value),
      },
    }));
  }

  function getMessageBasedOnState(state: string): string {
    switch (state) {
      case "alreadySubmitted":
        return "You've already responded to this form. Thanks!";
      case "expired":
        return expiredMessage ?? "";
      case "submitted":
        return "Thanks! Your response has been recorded.";
      case "error":
        return "Form not found or you don't have access.";
      default:
        return "";
    }
  }

  async function handleSubmit() {
    if (!id || !form) return;

    const missing = form.questions.filter((q) => q.isRequired && !isAnswered(q, answers[q.id]));
    const outOfRange = form.questions.filter((q) => {
      const value = answers[q.id]?.numericValue;
      return q.type === "Numeric" && value !== undefined && !isNumericInRange(value);
    });

    if (missing.length > 0 || outOfRange.length > 0) {
      setFieldErrors({
        ...Object.fromEntries(missing.map((q) => [q.id, "This question is required."])),
        ...Object.fromEntries(outOfRange.map((q) => [q.id, NUMERIC_RANGE_MESSAGE])),
      });
      return;
    }

    setFieldErrors({});
    setState("submitting");

    try {
      const submitted = await submitForm(id, {
        respondentToken,
        answers: form.questions
          .filter((q) => isAnswered(q, answers[q.id]))
          .map((q) => ({
            questionId: q.id,
            ...answers[q.id],
          })),
      });
      setResult(submitted);
      setState("submitted");
    } catch (err: unknown) {
      showError(getErrorMessage(err, "Failed to submit. Please try again."));
      setState("ready");
    }
  }

  if (state === "loading") {
    return (
      <BasePage>
        <div className="flex-1 flex items-center justify-center">
          <p className="text-gray-500">Loading form…</p>
        </div>
      </BasePage>
    );
  }

  const status = state == "error" || !form ? "error" : state;
  if (
    status == "alreadySubmitted" ||
    status == "expired" ||
    status == "submitted" ||
    status == "error" ||
    !form
  ) {
    const message = getMessageBasedOnState(state);
    const showScore =
      state === "submitted" && result?.totalScore != null && result.maxScore != null;

    return (
      <BasePage>
        <div className="flex-1 flex flex-col items-center justify-center gap-2">
          <p className="text-gray-600">{message}</p>
          {showScore && (
            <div className="text-center">
              <p className="text-sm text-gray-500">Your score</p>
              <p className="text-2xl font-semibold text-gray-900">{`${result.totalScore}/${result.maxScore}`}</p>
            </div>
          )}
        </div>
      </BasePage>
    );
  }

  return (
    <BasePage>
      <main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">
        <div>
          <h3 className="text-xl font-bold text-gray-900">{form.title}</h3>
          {form.description && <p className="mt-1 text-sm text-gray-500">{form.description}</p>}
        </div>

        {form.questions.map((q, i) => (
          <QuestionAnswerCard
            key={q.id}
            question={q}
            index={i}
            answer={answers[q.id]}
            error={fieldErrors[q.id]}
            onSingleChange={(optionId) => setSingleAnswer(q.id, optionId)}
            onMultipleToggle={(optionId) => toggleMultipleAnswer(q.id, optionId)}
            onTextChange={(value) => setTextAnswer(q.id, value)}
            onNumericChange={(value) => setNumericAnswer(q.id, value)}
          />
        ))}

        <Button onClick={handleSubmit} isLoading={state === "submitting"}>
          Submit
        </Button>
      </main>
    </BasePage>
  );
}
