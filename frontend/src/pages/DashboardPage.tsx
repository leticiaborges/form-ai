import { Button } from "../components/Button";
import { useAuth } from "../context/useAuth";

export function DashboardPage(){
  const { user, logout } = useAuth();
   return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center gap-6">
      <h1 className="text-3xl font-bold text-gray-900">Welcome, {user?.name ?? 'User'}!</h1>
      <p className="text-gray-500">Dashboard coming in Phase 3.</p>
      <Button variant="outline" onClick={logout}>Log out</Button>
    </div>
  );
}