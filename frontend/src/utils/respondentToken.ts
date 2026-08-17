const STORAGE_KEY = 'respondentToken';

export function getRespondentToken(): string {
    let token = localStorage.getItem(STORAGE_KEY);

    if (!token) {
        token = crypto.randomUUID();
        localStorage.setItem(STORAGE_KEY, token);
    }

    return token;
}