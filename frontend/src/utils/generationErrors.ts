import { isAxiosError } from "axios";
import type { MessageErrorResponse } from "../types/CustomResponse";
import { MAX_SOURCE_TEXT_LENGTH, MIN_SOURCE_TEXT_LENGTH } from "./sourceFile";

const TRY_LATER = "Form generation is unavailable right now. Please try again later.";
const TOO_LARGE = "The file is too large. The maximum size is 10 MB.";

// One message per code the generate endpoint can answer (ValidationErrorCode, AI-9).
const MESSAGES: Record<string, string> = {
  SourceFileUnsupported:
    "That file type isn't supported. Use a PDF, Word (.docx), PowerPoint (.pptx) or text (.txt) file.",
  SourceFileTooLarge: TOO_LARGE,
  SourceFileUnreadable:
    "We couldn't read that file. It may be damaged, password-protected or too long. Try another file.",
  SourceTextTooShort: `The text is too short to make questions from. Use at least ${MIN_SOURCE_TEXT_LENGTH} characters.`,
  SourceTextTooLong: `The text is too long. The maximum is ${MAX_SOURCE_TEXT_LENGTH.toLocaleString("en-US")} characters, pasted text and file together.`,
  GenerationOutputInvalid: "The AI returned a draft we couldn't use. Please try again.",
  GenerationUnavailable: TRY_LATER,
  GenerationBudgetReached: TRY_LATER,
};

export function getGenerateErrorMessage(err: unknown, fallback: string): string {
  if (!isAxiosError<MessageErrorResponse>(err)) return fallback;

  const code = err.response?.data?.code;
  if (code && MESSAGES[code]) return MESSAGES[code];

  // The request limit (about 11 MB) answers before the handler runs, with no code.
  if (err.response?.status === 413) return TOO_LARGE;

  // Everything else, a 429 included, carries its own message.
  return err.response?.data?.message ?? fallback;
}
