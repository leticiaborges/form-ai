import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "../context/useAuth";

export function RequireAuth() {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <Outlet /> : <Navigate to="/" replace />;
}
