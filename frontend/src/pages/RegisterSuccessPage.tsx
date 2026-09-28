import { useEffect, useState } from "react";
import axios from "axios";
import { Link, useLocation } from "react-router-dom";
import { resendVerificationEmail } from "../api/auth";
import { Button } from "../components/Button";

const COOLDOWN_SECONDS = 60;

export function RegisterSuccessPage() {
  const email = (useLocation().state as { email?: string } | null)?.email;
  const [isSending, setIsSending] = useState(false);
  const [secondsLeft, setSecondsLeft] = useState(0);
  const [notice, setNotice] = useState<{ text: string; isError: boolean } | null>(null);

  const isCoolingDown = secondsLeft > 0;
  useEffect(() => {
    if (!isCoolingDown) return;
    const timer = setInterval(() => setSecondsLeft((s) => Math.max(s - 1, 0)), 1000);
    return () => clearInterval(timer);
  }, [isCoolingDown]);

  async function resend() {
    if (!email) return;
    setIsSending(true);
    setNotice(null);
    try {
      await resendVerificationEmail(email);
      setNotice({
        text: "If an account exists for this email and isn't verified yet, we've sent a new link.",
        isError: false,
      });
      setSecondsLeft(COOLDOWN_SECONDS);
    } catch (err: unknown) {
      const rateLimited = axios.isAxiosError(err) && err.response?.status === 429;
      setNotice({
        text: rateLimited
          ? (err.response?.data?.message ?? "Too many requests. Please try again later.")
          : "Something went wrong. Please try again.",
        isError: true,
      });
    } finally {
      setIsSending(false);
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
        <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-brand-100">
          <svg
            className="h-8 w-8 text-brand-600"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={1.5}
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M21.75 6.75v10.5a2.25 2.25 0 01-2.25 2.25H4.5a2.25 2.25 0 01-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0019.5 4.5H4.5a2.25 2.25 0 00-2.25 2.25m19.5 0-9.75 6.75L2.25 6.75"
            />
          </svg>
        </div>
        <h1 className="text-2xl font-bold text-gray-900">Check your inbox</h1>
        <p className="mt-3 text-gray-600">
          We sent a verification email to your address. Click the link to activate your account.
        </p>
        <p className="mt-2 text-sm text-gray-400">
          The link expires in 15 minutes. Check your spam folder if you don't see it.
        </p>
        <div className="mt-6">
          <Link to="/login">
            <Button variant="outline" className="w-full">
              Go to login
            </Button>
          </Link>
        </div>
        {email && (
          <div className="mt-6">
            <p className="text-xs text-gray-500">
              Didn't get it? If nothing arrives in a few minutes, click below to send a new one.
            </p>
            <Button
              variant="outline"
              className="mt-2 w-full"
              isLoading={isSending}
              disabled={isCoolingDown}
              onClick={resend}
            >
              {isCoolingDown
                ? `Resend verification email (${secondsLeft}s)`
                : "Resend verification email"}
            </Button>
            {notice && notice.isError && (
              <p role="alert" className={`mt-2 text-xs text-red-600`}>
                {notice.text}
              </p>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
