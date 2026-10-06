// The numbers mirror the backend: FormSourceContent.MaxSourceTextLength / MaxFileBytes and
// GenerateFormHandler.MinSourceTextLength. Change them there and here together.
export const MAX_SOURCE_TEXT_LENGTH = 100_000;
export const MIN_SOURCE_TEXT_LENGTH = 100;
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

export const ACCEPTED_EXTENSIONS = [".pdf", ".docx", ".pptx", ".txt"] as const;
export const ACCEPT_ATTRIBUTE = ACCEPTED_EXTENSIONS.join(",");

export function fileExtension(fileName: string): string {
  const dot = fileName.lastIndexOf(".");
  return dot === -1 ? "" : fileName.slice(dot).toLowerCase();
}

export function isPdf(file: File): boolean {
  return fileExtension(file.name) === ".pdf";
}

export function validateSourceFile(file: File): string | null {
  const extension = fileExtension(file.name);

  if (!(ACCEPTED_EXTENSIONS as readonly string[]).includes(extension)) {
    return "That file type isn't supported. Use a PDF, Word (.docx), PowerPoint (.pptx) or text (.txt) file.";
  }
  if (file.size === 0) return "The file is empty.";
  if (file.size > MAX_FILE_BYTES)
    return `The file is too large. The maximum size is ${formatFileSize(MAX_FILE_BYTES)} MB.`;

  return null;
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
