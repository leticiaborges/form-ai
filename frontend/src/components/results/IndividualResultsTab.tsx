import { useEffect, useState } from "react";
import { getForm, getSubmissionDetail, getSubmissions } from "../../api/forms";
import type { FormDetail, FormQuestion } from "../../types/form";
import type { SubmissionDetail, SubmissionListItem } from "../../types/submissionReview";
import { QuestionAnswerCard } from "../respond/QuestionAnswerCard";
import type { AnswerPayload, QuestionGradingInfo } from "../../types/submission";
import { SubmissionPager } from "./SubmissionPager";
import { showError } from "../../utils/toast";
import { getErrorMessage } from "../../utils/getErrorMessage";

interface IndividualResultsTabProps {
  formId: string;
  reloadKey: number;
}

type PageState = 'loading' | 'ready' | 'error';

function buildAnswers(dataSubmissionDetail: SubmissionDetail): Record<string, AnswerPayload> {
  const answers: Record<string, AnswerPayload> = {};

  dataSubmissionDetail.answers.forEach((answer) => {
    const answerPayload: AnswerPayload = {
      selectedOptionTexts: answer.selectedOptions,
      textValue: answer.textValue ?? '',
      numericValue: answer.numericValue ?? undefined
    };

    answers[answer.questionId] = answerPayload;
  });

  return answers;
}

export function IndividualResultsTab({ formId, reloadKey }: Readonly<IndividualResultsTabProps>) {

  const [form, setForm] = useState<FormDetail | null>(null);
  const [state, setState] = useState<PageState>('loading');
  const [currentSubmissionIndex, setCurrentSubmissionIndex] = useState(1);
  const [totalSubmissionsCount, setTotalSubmissionsCount] = useState(0);
  const [currentSubmission, setCurrentSubmission] = useState<SubmissionDetail | null>(null);
  const [answers, setAnswers] = useState<Record<string, AnswerPayload>>({});
  const [chunkCache, setChunkCache] = useState<Record<number, SubmissionListItem[]>>({});
  const [detailCache, setDetailCache] = useState<Record<string, SubmissionDetail>>({});
  const defaultPageSize = 20;

  useEffect(() => {
    if (!formId)
      return;
    let ignore = false;

    async function runLoad() {
      try {
        const [formData, submissionsData] = await Promise.all([
          getForm(formId),
          getSubmissions(formId, 1, defaultPageSize)
        ]);

        if (ignore)
          return;

        setForm(formData);
        setState('ready');
        setTotalSubmissionsCount(submissionsData.totalCount);
        setCurrentSubmissionIndex(1);
        setChunkCache({ 1: submissionsData.items });

        if (submissionsData.items.length > 0) {
          const submissionId = submissionsData.items[0].submissionId;
          const submissionDetail = await getSubmissionDetail(formId, submissionId);
          if (ignore)
            return;

          setDetailCache({ [submissionId]: submissionDetail });
          setCurrentSubmission(submissionDetail);
          setAnswers(buildAnswers(submissionDetail));
        }
        else {
          setDetailCache({});
        }
      } catch (error: unknown) {
        showError(getErrorMessage(error, 'Failed to load results. Please try again.'));
        if (!ignore) setState('error');
      }
    }

    runLoad();

    return () => { ignore = true; }
  }, [formId, reloadKey]);

  function getGradingInfo(question: FormQuestion): QuestionGradingInfo | undefined {
    if (!form?.isGraded)
      return undefined;

    const answer = currentSubmission?.answers.find((item) => item.questionId === question.id);
    const correctOptionsText = question.options.filter((opt) => opt.isCorrect === true).map((opt) => opt.text);
    const hasAnswerKey = correctOptionsText.length > 0 || !!question.correctAnswer;

    const gradingInfo: QuestionGradingInfo = {
      points: question.points!,
      earnedScore: answer?.score ?? 0,
      isCorrect: answer?.isCorrect ?? false,
      correctOptionTexts: correctOptionsText,
      correctAnswerText: question.correctAnswer,
      hasAnswerKey: hasAnswerKey
    };

    return gradingInfo;
  }

  async function onPageChange(page: number) {
    setCurrentSubmissionIndex(page);

    const chunk = Math.ceil(page / defaultPageSize);
    const offset = (page - 1) % defaultPageSize;
    const submissions = await getOrFetchChunk(chunk);

    const submissionId = submissions[offset].submissionId;

    const submissionDetail = await getOrFetchSubmissionDetail(submissionId);
    setCurrentSubmission(submissionDetail);
    setAnswers(buildAnswers(submissionDetail));
  }

  async function getOrFetchChunk(chunk: number): Promise<SubmissionListItem[]> {
    if (chunkCache[chunk])
      return chunkCache[chunk];

    const pageData = await getSubmissions(formId, chunk, defaultPageSize);
    setTotalSubmissionsCount(pageData.totalCount);
    setChunkCache(prev => ({ ...prev, [chunk]: pageData.items }));

    return pageData.items;
  }

  async function getOrFetchSubmissionDetail(submissionId: string): Promise<SubmissionDetail> {
    if (detailCache[submissionId])
      return detailCache[submissionId];

    const submissionDetail = await getSubmissionDetail(formId, submissionId);
    setDetailCache((prev) => ({ ...prev, [submissionId]: submissionDetail }));
    return submissionDetail;
  }

  if (state === 'loading')
    return <p className="py-16 text-center text-gray-500">Loading results…</p>;

  if (state === 'error')
    return <p className="py-16 text-center text-red-600">Could not load the results.</p>;

  if (totalSubmissionsCount === 0) {
    return (
      <div className="py-16 text-center text-gray-400">
        <p className="text-lg">No submissions yet.</p>
        <p className="mt-1 text-sm">Share the link and answers will show up here.</p>
      </div>
    );
  }

  return (
    <div>
      {totalSubmissionsCount > 0 && form &&

        <main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">

          <SubmissionPager page={currentSubmissionIndex} totalPages={totalSubmissionsCount} onPageChange={onPageChange} />

          {form.questions.map((q, i) => (
            <QuestionAnswerCard
              key={q.id}
              question={q}
              index={i}
              readOnly={true}
              answer={answers[q.id]}
              grading={getGradingInfo(q)}
            />
          ))}

        </main>
      }
    </div>
  );
}