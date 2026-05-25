export type QuestionType = 'Single' | 'Multiple' | 'Text' | 'Numeric';

export interface FormOption {
    id: string;
    text: string;
    order: number;
    isCorrect: boolean;
}

export interface FormQuestion {
    id: string;
    text: string;
    type: QuestionType;
    order: number;
    isRequired: boolean;
    aiGenerated: boolean;
    points: number | null;
    correctAnswer: string | null;
    options: FormOption[]
}

export interface FormDetail {
    id: string;
    title: string;
    description: string;
    isPublic: boolean;
    expiresAt: string | null;
    showResultsAfterSubmit: boolean;
    createdAt: string;
    questions: FormQuestion[];
}