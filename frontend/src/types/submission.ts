import type { QuestionType } from "./form";

export interface AnswerOption {
  id: string;
  text: string;
  order: number;
}

export interface AnswerQuestion {
  id: string;
  text: string;
  type: QuestionType;
  order: number;
  isRequired: boolean;
  options: AnswerOption[];
}

export interface AnswerForm {
  id: string;
  title: string;
  description: string | null;
  questions: AnswerQuestion[];
}

export interface MySubmission {
  hasSubmitted: boolean;
  submittedAt: string | null;
}

export interface AnswerPayload {
  selectedOptionIds?: string[];
  selectedOptionTexts?: string[];
  textValue?: string;
  numericValue?: number;
}

export interface QuestionGradingInfo {
  points: number;
  earnedScore: number;
  isCorrect: boolean;
  correctOptionTexts: string[];
  correctAnswerText: string | null;
  hasAnswerKey: boolean;
}

export interface SubmitFormPayload {
  respondentToken: string;
  answers: (AnswerPayload & { questionId: string })[];
}

export interface SubmitFormResult {
  id: string;
  /** Both scores are null unless the form is graded and its owner chose to show them. */
  totalScore: number | null;
  maxScore: number | null;
}
