import type { QuestionResult } from "../../types/results";

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
      <ul className="flex flex-col gap-1 text-sm text-gray-600">
        {isSelection ?
          question.options.map((option) => (
            <li key={option.text}>{option.text} - {option.count}</li>
          ))
          :
          question.values.map((value) => (
            <li key={value.value}>{value.value} - {value.count}</li>
          ))}
      </ul>
    )}

  </section>
  );

}