import type { QuestionType } from "./form";

export interface OptionResult {
    text: string;
    count: number;
    isCorrect: boolean | null;
}

export interface ValueResult {
    value: string;
    count: number;
    isCorrect: boolean | null;
}

export interface ScoreBucket {
    score: number;
    submissionCount: number;
}

export interface QuestionResult {
    questionId: string;
    text: string;
    type: QuestionType;
    order: number;
    answerCount: number;
    points?: number | null;
    correctAnswerCount?: number | null;
    options: OptionResult[];
    values: ValueResult[];
}

export interface FormResults {
    formId: string;
    title: string;
    isGraded: boolean;
    submissionCount: number;
    totalPoints: number | null;
    scoreDistribution: ScoreBucket[];
    questions: QuestionResult[];
}