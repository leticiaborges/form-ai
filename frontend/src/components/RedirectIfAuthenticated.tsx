import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "../context/useAuth";

export function RedirectIfAuthenticated() {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <Navigate to="/dashboard" replace /> : <Outlet />;
}
