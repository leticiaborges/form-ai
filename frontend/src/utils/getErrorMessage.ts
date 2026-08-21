import type { CustomResponse } from '../types/CustomResponse';

export function getErrorMessage(err: unknown, fallback: string): string {
    const e = err as CustomResponse;
    return e.response?.data?.message ?? fallback;
}


