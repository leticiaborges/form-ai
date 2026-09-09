export const DATETIME_FORMATS = {
    /** Hours and minutes only: "2026-09-20T14:30" */
    DATETIME_HHMM: 'datetime-hhmm',
    /** Hours, minutes, and seconds: "2026-09-20T14:30:45" */
    DATETIME_HHMMSS: 'datetime-hhmmss',
} as const;

type DateTimeFormat = typeof DATETIME_FORMATS[keyof typeof DATETIME_FORMATS];

export function utcToLocalDateTime(utcString: string | null, timeFormat: DateTimeFormat = DATETIME_FORMATS.DATETIME_HHMM): string {
    if (!utcString) return '';
    const date = new Date(utcString);
    return getDefaultFormatStringDateTime(date, timeFormat);
}

export function getDefaultFormatStringDateTime(date: Date | null, timeFormat: DateTimeFormat = DATETIME_FORMATS.DATETIME_HHMM): string {
    if (!date) return '';
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    const seconds = String(date.getSeconds()).padStart(2, '0');

    switch (timeFormat) {
        case DATETIME_FORMATS.DATETIME_HHMM:
            return `${year}-${month}-${day}T${hours}:${minutes}`;
        case DATETIME_FORMATS.DATETIME_HHMMSS:
            return `${year}-${month}-${day}T${hours}:${minutes}:${seconds}`;
        default:
            return `${year}-${month}-${day}T${hours}:${minutes}`;
    }
}

export function addDays(date: Date, days: number) {
    var result = new Date(date);
    result.setDate(result.getDate() + days);
    return result;
}
