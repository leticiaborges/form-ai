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

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/register/success" element={<RegisterSuccessPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route path="/forms/new" element={<CreateFormPage />} />
      <Route path="/forms/:id/edit" element={<FormEditorPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
      <Route path="/forms/:id/answer" element={<FormAnswerPage />} />
    </Routes>
  );
}
