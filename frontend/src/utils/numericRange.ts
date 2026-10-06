// Mirrors the backend's numeric limit for answers and suggested answers (change both together).
export const MAX_NUMERIC_VALUE = 1_000_000_000_000;

export const NUMERIC_RANGE_MESSAGE =
  "Enter a number between -1,000,000,000,000 and 1,000,000,000,000.";

export function isNumericInRange(value: number): boolean {
  return Number.isFinite(value) && Math.abs(value) <= MAX_NUMERIC_VALUE;
}
