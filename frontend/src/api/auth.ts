import api from './axios';
import type { LoginResponse, RegisterUserResponse, RegisterUserRequest } from '../types/auth';   

export async function registerUser(data: RegisterUserRequest): Promise<RegisterUserResponse> {
    const response = await api.post<RegisterUserResponse>('/auth/register', data);
    return response.data;
}

export async function loginUser(data: { 
    email: string,
    password: string
}) : Promise<LoginResponse> {
    const response = await api.post<LoginResponse>('/auth/login', data);
    return response.data;
}

export async function verifyEmail(token: string) : Promise<void> {
    await api.post('/auth/verify-email', { token });
}