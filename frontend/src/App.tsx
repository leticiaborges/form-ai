import { Routes, Route, Navigate } from "react-router-dom";
import { LandingPage } from "./pages/LandingPage";
import { RegisterPage } from "./pages/RegisterPage";
import { RegisterSuccessPage } from "./pages/RegisterSuccessPage";
import { VerifyEmailPage } from "./pages/VerifyEmailPage";
import { LoginPage } from "./pages/LoginPage";
import { DashboardPage } from "./pages/DashboardPage";
import { FormEditorPage } from "./pages/FormEditorPage";
import { CreateFormPage } from "./pages/CreateFormPage";
import { FormAnswerPage } from "./pages/FormAnswerPage";
import { RequireAuth } from "./components/RequireAuth";
import { RedirectIfAuthenticated } from "./components/RedirectIfAuthenticated";

export default function App() {
  return (
    <Routes>
      <Route element={<RedirectIfAuthenticated />}>
        <Route path="/" element={<LandingPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/login" element={<LoginPage />} />
      </Route>
      <Route path="/register/success" element={<RegisterSuccessPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route element={<RequireAuth />}>
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/forms/new" element={<CreateFormPage />} />
        <Route path="/forms/:id/edit" element={<FormEditorPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
      <Route path="/forms/:id/answer" element={<FormAnswerPage />} />
    </Routes>
  );
}
