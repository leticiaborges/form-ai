import { Link } from 'react-router-dom';
import { Button } from '../components/Button';

export function LandingPage(){
    return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-50 to-white flex flex-col">
      <header className="flex items-center justify-between px-6 py-4 max-w-6xl mx-auto w-full">
        <span className="text-2xl font-bold text-indigo-600">FormAI</span>
        <nav className="flex gap-3">
          <Link to="/login"><Button variant="outline">Log in</Button></Link>
          <Link to="/register"><Button>Get started</Button></Link>
        </nav>
      </header>

      <main className="flex flex-col items-center justify-center flex-1 text-center px-6 gap-8">
        <div className="max-w-2xl">
          <h1 className="text-5xl font-extrabold text-gray-900 leading-tight">
            Build forms <span className="text-indigo-600">powered by AI</span>
          </h1>
          <p className="mt-4 text-lg text-gray-600">
            Describe what you need. FormAI generates smart, beautiful forms in
            seconds — ready to share.
          </p>
        </div>
        <div className="flex gap-4 flex-wrap justify-center">
          <Link to="/register">
            <Button className="px-8 py-3 text-base">Start for free</Button>
          </Link>
          <Link to="/login">
            <Button variant="outline" className="px-8 py-3 text-base">Log in</Button>
          </Link>
        </div>
      </main>

      <footer className="text-center text-sm text-gray-400 py-6">
        © {new Date().getFullYear()} FormAI. All rights reserved.
      </footer>
    </div>
  );
}