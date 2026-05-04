export interface CustomResponse {
    response?: DataResponse;
}

export interface DataResponse {
    data?: MessageErrorResponse
}

export interface MessageErrorResponse {
    message?: string;
    errors?: Record<string, string[]> ;
}