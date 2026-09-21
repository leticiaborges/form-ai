export interface AuthUser {
  id: string;
  name: string;
  email: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
}

export interface RegisterUserResponse {
  id: string;
  name: string;
  email: string;
}

export interface RegisterUserRequest {
  name: string;
  email: string;
  password: string;
}
