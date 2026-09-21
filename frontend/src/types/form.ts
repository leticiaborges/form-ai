export type QuestionType = "Single" | "Multiple" | "Text" | "Numeric";

export interface FormSummary {
  id: string;
  title: string;
  isPublic: boolean;
  expiresAt: string | null;
  createdAt: string;
  submissionCount: number;
}

export interface FormOption {
  id: string;
  text: string;
  order: number;
  isCorrect: boolean | null;
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
  options: FormOption[];
}

export interface FormDetail {
  id: string;
  title: string;
  description: string;
  isPublic: boolean;
  expiresAt: string | null;
  showResultsAfterSubmit: boolean;
  isGraded: boolean;
  createdAt: string;
  questions: FormQuestion[];
}
