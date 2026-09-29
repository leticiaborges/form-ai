import { Link } from "react-router-dom";
import { Button } from "../components/Button";
import { useStartDemo } from "../hooks/useStartDemo";
import formAiLogo from "../assets/FormAI.png";

export function LandingPage() {
  const { startDemo, isStarting, error: demoError } = useStartDemo();

  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 to-white flex flex-col">
      <header className="flex items-center justify-between px-6 py-4 max-w-6xl mx-auto w-full">
        <img src={formAiLogo} alt="FormAI" className="h-16 w-auto" />
        <nav className="flex gap-3">
          <Link to="/login">
            <Button variant="outline">Log in</Button>
          </Link>
          <Link to="/register">
            <Button>Get started</Button>
          </Link>
        </nav>
      </header>

      <main className="flex flex-col items-center justify-center flex-1 text-center px-6 gap-8">
        <div className="max-w-2xl">
          <h1 className="text-5xl text-gray-900 leading-tight">
            Build forms <span className="text-brand-600">with AI</span>
          </h1>
          <p className="mt-4 text-lg text-gray-600">
            Paste your content. FormAI turns it into a form in seconds — ready to edit and share.
          </p>
        </div>
        <div className="flex gap-4 flex-wrap justify-center">
          <Button
            isLoading={isStarting}
            onClick={startDemo}
            className="px-8 py-3 text-base shadow-lg ring-4 ring-brand-200"
          >
            Try demo
          </Button>
          <Link to="/login">
            <Button variant="outline" className="px-8 py-3 text-base">
              Log in
            </Button>
          </Link>
          <Link to="/register">
            <Button variant="outline" className="px-8 py-3 text-base">
              Get started
            </Button>
          </Link>
        </div>
        {demoError && (
          <p role="alert" className="text-sm text-red-700">
            {demoError}
          </p>
        )}
      </main>

      <footer className="text-center text-sm text-gray-400 py-6">
        © {new Date().getFullYear()} FormAI. All rights reserved.
      </footer>
    </div>
  );
}
