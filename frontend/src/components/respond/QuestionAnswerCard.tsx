import type { AnswerPayload, AnswerQuestion } from "../../types/submission";

interface AnswerQuestionCardProps {
    question: AnswerQuestion;
    index: number;
    answer?: AnswerPayload;
    error?: string;
    onSingleChange: (optionId: string) => void;
    onMultipleToggle: (optionId: string) => void;
    onTextChange: (value: string) => void;
    onNumericChange: (value: string) => void;
}

export function QuestionAnswerCard({
    question, index, answer, error,
    onSingleChange, onMultipleToggle,
    onTextChange, onNumericChange
}: Readonly<AnswerQuestionCardProps>) {
    return (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
            <p className="text-question font-medium text-gray-900">
                {index + 1}. {question.text}
                {question.isRequired && <span className="text-red-500"> *</span>}
            </p>

            <div className="mt-3 flex flex-col gap-2">
                {question.type === 'Single' && question.options.map(opt => (
                    <label key={opt.id} className="flex items-center gap-2 text-option text-gray-700">
                        <input
                            type="radio"
                            name={question.id}
                            checked={answer?.selectedOptionIds?.[0] === opt.id}
                            onChange={() => onSingleChange(opt.id)}
                            className="text-brand-600 focus:ring-brand-500"
                        />
                        {opt.text}
                    </label>
                ))}

                {question.type === 'Multiple' && question.options.map(opt => (
                    <label key={opt.id} className="flex items-center gap-2 text-option text-gray-700">
                        <input
                            type="checkbox"
                            checked={answer?.selectedOptionIds?.includes(opt.id) ?? false}
                            onChange={() => onMultipleToggle(opt.id)}
                            className="rounded text-brand-600 focus:ring-brand-500"
                        />
                        {opt.text}
                    </label>
                ))}

                {question.type === 'Text' && (
                    <textarea
                        rows={3}
                        value={answer?.textValue ?? ''}
                        onChange={e => onTextChange(e.target.value)}
                        className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                                   focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
                    />
                )}

                {question.type === 'Numeric' && (
                    <input
                        type="number"
                        value={answer?.numericValue ?? ''}
                        onChange={e => onNumericChange(e.target.value)}
                        className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                                   focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
                    />
                )}
            </div>

            {error && <p className="mt-2 text-xs text-red-500">{error}</p>}
        </div>
    );
}