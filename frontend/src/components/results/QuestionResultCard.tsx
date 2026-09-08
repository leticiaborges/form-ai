import type { QuestionResult } from "../../types/results";
import { OptionBarChart } from "./OptionBarChart";
import { ValueAnswerList } from "./ValueAnswerList";

interface QuestionResultCardProps {
  question: QuestionResult;
}

export function QuestionResultCard({ question }: Readonly<QuestionResultCardProps>) {
  const { answerCount } = question;

  const isSelection = question.type == 'Single' || question.type == 'Multiple';

  return (<section className="bg-white rounded-2xl shadow-md p-6 flex flex-col gap-3">
    <h3 className="text-base font-medium text-gray-900">{question.text}</h3>
    <p className="text-xs text-gray-500">
      {answerCount === 0 ? 'No answers yet' :
        `${answerCount} answer${answerCount !== 1 ? 's' : ''}`}
    </p>

    {answerCount > 0 && (
      isSelection ?
        <OptionBarChart options={question.options} answerCount={answerCount} />
        :
        <ValueAnswerList values={question.values} />
    )}

  </section>
  );

}