import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate } from "react-router-dom";
import z from "zod";
import { loginUser } from "../api/auth";
import type { CustomResponse } from "../types/CustomResponse";
import { Input } from "../components/Input";
import { Button } from "../components/Button";
import { useAuth } from "../context/useAuth";

const loginSchema = z.object({
  email: z.email("Enter a valid email address"),
  password: z.string().min(1, 'Password is required')
});

type LoginFormData = z.infer<typeof loginSchema>;

export function LoginPage() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [ serverError, setServerError ] = useState<string | null>(null);

  const { register, handleSubmit, formState: { errors, isSubmitting }} =
    useForm<LoginFormData>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(data: LoginFormData){
    setServerError(null);

    try{
      const response = await loginUser(data);
      const payload = JSON.parse(atob(response.accessToken.split('.')[1]));

      login(payload.accessToken, response.refreshToken, {
        id: payload.sub,
        name: payload.name,
        email: payload.email,
      });

      navigate('/dashboard');
    }
    catch(err: unknown){
      const e = err as CustomResponse;
      setServerError(e.response?.data?.message ?? 'Login failed. Check your credentials.');
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8">
        <div className="text-center mb-6">
          <Link to="/" className="text-2xl font-bold text-indigo-600">FormAI</Link>
          <h1 className="mt-4 text-xl font-semibold text-gray-900">Welcome back</h1>
          <p className="text-sm text-gray-500 mt-1">
            Don't have an account?{' '}
            <Link to="/register" className="text-indigo-600 hover:underline">Sign up</Link>
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <Input label="Email" type="email" autoComplete="email"
            error={errors.email?.message} {...register('email')} />
          <Input label="Password" type="password" autoComplete="current-password"
            error={errors.password?.message} {...register('password')} />

          {serverError && (
            <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
              {serverError}
            </div>
          )}

          <Button type="submit" isLoading={isSubmitting} className="mt-2 w-full">
            Log in
          </Button>
        </form>
      </div>
    </div>
  );
}