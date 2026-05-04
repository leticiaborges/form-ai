import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { verifyEmail } from "../api/auth";
import { Button } from "../components/Button";
import type { CustomResponse } from "../types/CustomResponse";

type PageState =
  | { status: 'loading' }
  | { status: 'success' }
  | { status: 'error'; errorMessage: string };

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');

  const [state, setState] = useState<PageState>(
    token ? { status: 'loading' } : { status: 'error', errorMessage: 'No token found in the URL' }
  );

  useEffect(() => {
    if (!token) return;

    verifyEmail(token)
      .then(() => setState({ status: 'success' }))
      .catch((err: unknown) => {
        const e = err as CustomResponse;
        setState({
          status: 'error',
          errorMessage: e.response?.data?.message ?? 'Verification failed. The link may have expired.',
        });
      });
  }, [token]);

  if (state.status === 'loading') {
     return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <svg className="animate-spin h-10 w-10 text-indigo-600" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
          </svg>
          <p className="text-lg font-medium text-gray-700">Verifying your email…</p>
        </div>
      </div>
    );
  }

  if (state.status === 'success') {
    return (
      <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
        <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-green-100">
            <svg className="h-8 w-8 text-green-600" fill="none" viewBox="0 0 24 24"
              stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg>
          </div>
          <h1 className="text-2xl font-bold text-gray-900">Email verified!</h1>
          <p className="mt-3 text-gray-600">Your account is now active. You can log in.</p>
          <div className="mt-6">
            <Link to="/login"><Button className="w-full">Go to login</Button></Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
        <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-red-100">
          <svg className="h-8 w-8 text-red-600" fill="none" viewBox="0 0 24 24"
            stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </div>
        <h1 className="text-2xl font-bold text-gray-900">Verification failed</h1>
        <p className="mt-3 text-gray-600">{state.status === 'error' && state.errorMessage}</p>
        <div className="mt-6 flex flex-col gap-3">
          <Link to="/register">
            <Button variant="outline" className="w-full">Register again</Button>
          </Link>
        </div>
      </div>
    </div>
  );
}