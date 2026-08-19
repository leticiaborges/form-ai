import { useEffect, useState } from "react";
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { useAuth } from "../context/useAuth";
import type { FormSummary } from "../types/form";
import { useNavigate } from "react-router-dom";
import { listForms } from "../api/forms";
import { FormCard } from "../components/FormCard";
import { SubmissionsPerFormChart } from "../components/SubmissionsPerFormChart";


type PageState = 'loading' | 'ready' | 'error';

export function DashboardPage() {
  const { user } = useAuth();
  const navigate = useNavigate();

  const [state, setState] = useState<PageState>('loading');
  const [forms, setForms] = useState<FormSummary[]>([]);

  useEffect(() => {
    listForms().then(data => {
      setForms(data);
      setState('ready');
    }).catch(() => setState('error'));
  }, []);


  return (
    <BasePage>
      <header className="border-b border-gray-200 bg-white px-6 py-2 shadow-sm flex items-center justify-between">
        <h3 className="text-xl font-bold text-gray-900">Welcome, {user?.name ?? 'User'}!</h3>
        <Button onClick={() => navigate('/forms/new')}>New form</Button>
      </header>

      <main className="mx-auto max-w-4xl px-4 py-8">
        <h4 className="text-lg font-semibold text-gray-900 mb-4">Submissions per form</h4>
        {state === 'ready' && <SubmissionsPerFormChart forms={forms} />}
        <div style={{ marginTop: "10px" }}>
          <h4 className="text-lg font-semibold text-gray-900 mb-4">My forms</h4>

          {state === 'loading' && (
            <p className="text-gray-500">Loading your forms…</p>
          )}

          {state === 'error' && (
            <p className="text-red-600">Couldn't load your forms. Please try again later.</p>
          )}

          {state === 'ready' && forms.length === 0 && (
            <div className="py-16 text-center text-gray-400">
              <p className="text-lg">You haven't created any forms yet.</p>
            </div>
          )}

          {state === 'ready' && forms.length > 0 && (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {forms.map(form => (
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
    </BasePage >
  );
}
