import { Link } from "react-router-dom";
import { Button } from "../components/Button";

export function RegisterSuccessPage(){
   return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex items-center justify-center px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-md p-8 text-center">
        <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-indigo-100">
          <svg className="h-8 w-8 text-indigo-600" fill="none" viewBox="0 0 24 24"
            stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round"
              d="M21.75 6.75v10.5a2.25 2.25 0 01-2.25 2.25H4.5a2.25 2.25 0 01-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0019.5 4.5H4.5a2.25 2.25 0 00-2.25 2.25m19.5 0-9.75 6.75L2.25 6.75" />
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
            <Button variant="outline" className="w-full">Go to login</Button>
          </Link>
        </div>
      </div>
    </div>
  );
}