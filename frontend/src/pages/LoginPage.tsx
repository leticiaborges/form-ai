import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate } from "react-router-dom";
import z from "zod";
import { loginUser } from "../api/auth";
import { useStartDemo } from "../hooks/useStartDemo";
import type { CustomResponse } from "../types/CustomResponse";
import { Input } from "../components/Input";
import { Button } from "../components/Button";
import { useAuth } from "../context/useAuth";
import { userFromAccessToken } from "../auth/tokenStore";
import formAiLogo from "../assets/FormAI.png";

const loginSchema = z.object({
  email: z.email("Enter a valid email address"),
  password: z.string().min(1, "Password is required"),
});

type LoginFormData = z.infer<typeof loginSchema>;

export function LoginPage() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [serverError, setServerError] = useState<string | null>(null);
  const { startDemo, isStarting, error: demoError } = useStartDemo();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormData>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(data: LoginFormData) {
    setServerError(null);

    try {
      const { accessToken } = await loginUser(data);
      login(accessToken, userFromAccessToken(accessToken));

      navigate("/dashboard");
    } catch (err: unknown) {
      const e = err as CustomResponse;
      setServerError(e.response?.data?.message ?? "Login failed. Check your credentials.");
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8">
        <div className="text-center mb-6">
          <Link to="/" className="inline-flex justify-center">
            <img src={formAiLogo} alt="FormAI" className="h-16 w-auto" />
          </Link>
          <h1 className="mt-4 text-2xl font-semibold text-gray-900">Welcome back</h1>
          <p className="text-sm text-gray-500 mt-1">
            Don't have an account?{" "}
            <Link to="/register" className="text-brand-600 hover:underline">
              Sign up
            </Link>
          </p>
        </div>

        <Button
          type="button"
          isLoading={isStarting}
          onClick={() => {
            setServerError(null);
            startDemo();
          }}
          className="w-full py-3 text-base shadow-md"
        >
          Try demo
        </Button>
        <p className="my-4 text-center text-xs uppercase tracking-wide text-gray-400">
          or log in with your account
        </p>

        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <Input
            label="Email"
            type="email"
            autoComplete="email"
            error={errors.email?.message}
            {...register("email")}
          />
          <Input
            label="Password"
            type="password"
            autoComplete="current-password"
            error={errors.password?.message}
            {...register("password")}
          />

          {(serverError ?? demoError) && (
            <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
              {serverError ?? demoError}
            </div>
          )}

          <Button type="submit" variant="outline" isLoading={isSubmitting} className="mt-2 w-full">
            Log in
          </Button>
        </form>
      </div>
    </div>
  );
}
