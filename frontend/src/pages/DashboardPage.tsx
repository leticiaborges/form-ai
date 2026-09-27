import { useEffect, useState } from "react";
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { useAuth } from "../context/useAuth";
import type { FormSummary } from "../types/form";
import { useNavigate } from "react-router-dom";
import { listForms } from "../api/forms";
import { FormCard } from "../components/FormCard";
import { SubmissionsPerFormChart } from "../components/SubmissionsPerFormChart";

type PageState = "loading" | "ready" | "error";

export function DashboardPage() {
  const { user } = useAuth();
  const navigate = useNavigate();

  const [state, setState] = useState<PageState>("loading");
  const [forms, setForms] = useState<FormSummary[]>([]);

  useEffect(() => {
    listForms()
      .then((data) => {
        setForms(data);
        setState("ready");
      })
      .catch(() => {
        setState("error");
      });
  }, []);

  const dashboardHeader = (
    <header className="border-b border-gray-200 bg-white px-6 py-2 shadow-sm flex items-center justify-between">
      <h3 className="text-xl font-bold text-gray-900">Welcome, {user?.name ?? "User"}!</h3>
      <Button onClick={() => navigate("/forms/new")}>New form</Button>
    </header>
  );

  return (
    <BasePage stickyHeader={dashboardHeader}>
      <main className="mx-auto max-w-4xl px-4 py-8">
        {state === "ready" && forms.length > 0 && (
          <>
            <h4 className="text-lg font-semibold text-gray-900 mb-4">Submissions per form</h4>
            <SubmissionsPerFormChart
              forms={forms}
              onSelectForm={(formId) => navigate(`/forms/${formId}/edit`)}
            />
          </>
        )}
        <div style={{ marginTop: "10px" }}>
          <h4 className="text-lg font-semibold text-gray-900 mb-4">My forms</h4>

          {state === "loading" && <p className="text-gray-500">Loading your forms…</p>}

          {state === "error" && (
            <p className="text-red-600">Couldn't load your forms. Please try again later.</p>
          )}

          {state === "ready" && forms.length === 0 && (
            <div className="bg-white rounded-2xl shadow-md p-10 text-center">
              <p className="text-lg text-gray-600">You haven't created any forms yet.</p>
              <Button className="mt-4" onClick={() => navigate("/forms/new")}>
                Create your first form
              </Button>
            </div>
          )}

          {state === "ready" && forms.length > 0 && (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {forms.map((form) => (
                <FormCard
                  key={form.id}
                  form={form}
                  onClick={() => navigate(`/forms/${form.id}/edit`)}
                />
              ))}
            </div>
          )}
        </div>
      </main>
    </BasePage>
  );
}
