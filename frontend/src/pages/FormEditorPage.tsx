import { useEffect, useState, type ReactNode } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import type { FormDetail, FormQuestion } from "../types/form";
import { getForm, saveFormEditor, deleteForm, getSubmissionCount } from "../api/forms";
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { getErrorMessage } from "../utils/getErrorMessage";
import { showSuccess, showError } from "../utils/toast";
import { DeleteFormModal } from "../components/editors/DeleteFormModal";
import { DEFAULT_POINTS } from "../components/editors/PointsInput";
import { Tabs, type TabDefinition } from "../components/Tabs";
import { FormEditorTab } from "../components/FormEditorTab";
import { ResultsTab } from "../components/results/ResultsTab";
import { DATETIME_FORMATS, utcToLocalDateTime } from "../utils/dateUtils";
import { FormConfigTab } from "../components/FormConfigTab";

type PageState = "loading" | "ready" | "saving" | "deleting" | "error";

export function FormEditorPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [state, setState] = useState<PageState>("loading");
  const [form, setForm] = useState<FormDetail | null>(null);
  const [questions, setQuestions] = useState<FormQuestion[]>([]);
  const [searchParams, setSearchParams] = useSearchParams();

  const TABS: TabDefinition[] = [
    { id: "editor", label: "Edit form" },
    { id: "results", label: "Results" },
    { id: "config", label: "Configuration" },
  ];

  const activeTab = TABS.find((t) => t.id === searchParams.get("tab"))?.id ?? "editor";

  function changeTab(id: string) {
    setSearchParams(id == "editor" ? {} : { tab: id }, { replace: true });
  }

  const [isPublic, setIsPublic] = useState(false);
  const [isGraded, setIsGraded] = useState(false);
  const [showResultsAfterSubmit, setShowResultsAfterSubmit] = useState(false);
  const [title, setTitle] = useState("");
  const [titleError, setTitleError] = useState("");

  const [expiresAt, setExpiresAt] = useState("");
  const [expiresAtError, setExpiresAtError] = useState("");

  const [description, setDescription] = useState("");
  const [descriptionError, setDescriptionError] = useState("");

  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [submissionCount, setSubmissionCount] = useState<number | null>(null);

  const [resultsReloadKey, setResultsReloadKey] = useState(0);

  function setFormData(data: FormDetail) {
    setForm(data);
    setQuestions(data.questions);
    setTitle(data.title);
    setDescription(data.description ?? "");
    setIsPublic(data.isPublic);
    setIsGraded(data.isGraded);
    setShowResultsAfterSubmit(data.showResultsAfterSubmit);
    setExpiresAt(utcToLocalDateTime(data.expiresAt ?? "", DATETIME_FORMATS.DATETIME_HHMM));
  }

  useEffect(() => {
    if (!id) return;

    getForm(id)
      .then((data) => {
        setFormData(data);
        setState("ready");
      })
      .catch(() => setState("error"));
  }, [id]);

  function toggleGraded(next: boolean) {
    setIsGraded(next);

    // Only a graded form has a score to show. Ticking "Graded form" again does not bring the
    // flag back: the owner has to choose it again.
    if (!next) setShowResultsAfterSubmit(false);

    if (next)
      setQuestions((qs) =>
        qs.map((q) => (q.points === null ? { ...q, points: DEFAULT_POINTS } : q)),
      );
  }

  function openDeleteModal() {
    if (!id) return;

    setSubmissionCount(null);
    setShowDeleteModal(true);

    getSubmissionCount(id)
      .then((count) => setSubmissionCount(count))
      .catch(() => setSubmissionCount(null));
  }

  async function handleDelete() {
    if (!id) return;

    setState("deleting");

    try {
      await deleteForm(id);

      showSuccess("Form deleted");
      navigate("/dashboard", { replace: true });
    } catch (err: unknown) {
      showError(getErrorMessage(err, "Failed to delete the form. Please try again."));
      setState("ready");
      setShowDeleteModal(false);
    }
  }

  async function handleSave() {
    if (!id) return;

    setExpiresAtError("");

    const trimmedTitle = title.trim();
    if (!trimmedTitle) {
      setTitleError("Title is required.");
      return;
    }
    if (trimmedTitle.length > 255) {
      setTitleError("Title must be at most 255 characters.");
      return;
    }

    if (!expiresAt) {
      setExpiresAtError("Pick an expiry date and time.");
      return;
    }

    if (Number.isNaN(Date.parse(expiresAt))) {
      setExpiresAtError("Enter a valid date and time.");
      return;
    }

    if (new Date(expiresAt) <= new Date()) {
      setExpiresAtError("The expiry must be in the future.");
      return;
    }

    const trimmedDescription = description.trim();
    if (trimmedDescription.length > 1024) {
      setDescriptionError("Description must be at most 1024 characters.");
      return;
    }

    setState("saving");

    try {
      await saveFormEditor(id, {
        title: trimmedTitle,
        description: trimmedDescription,
        isPublic,
        isGraded,
        showResultsAfterSubmit,
        questions,
        expiresAt: new Date(expiresAt),
      });

      const refreshedForm = await getForm(id);
      setFormData(refreshedForm);
      setResultsReloadKey((k) => k + 1);

      showSuccess("Form saved.");
      setState("ready");
    } catch (err: unknown) {
      showError(getErrorMessage(err, "Failed to save. Please try again."));
      setState("ready");
    }
  }

  if (state === "loading") {
    return (
      <BasePage>
        <div className="flex-1 flex items-center justify-center">
          <p className="text-gray-500">Loading form…</p>
        </div>
      </BasePage>
    );
  }

  if (state === "error" || !form) {
    return (
      <BasePage>
        <div className="flex-1 flex items-center justify-center">
          <p className="text-red-600">Form not found or you don't have access.</p>
        </div>
      </BasePage>
    );
  }

  let currentTab: ReactNode = null;
  switch (activeTab) {
    case "editor":
      currentTab = (
        <FormEditorTab
          form={form}
          questions={questions}
          onQuestionsChange={setQuestions}
          description={description}
          onDescriptionChange={(value) => {
            setDescription(value);
            setDescriptionError("");
          }}
          descriptionError={descriptionError}
          isPublic={isPublic}
          onIsPublicChange={setIsPublic}
          isGraded={isGraded}
          onIsGradedChange={toggleGraded}
        />
      );
      break;
    case "results":
      currentTab = <ResultsTab formId={form.id} reloadKey={resultsReloadKey} />;
      break;
    case "config":
      currentTab = (
        <FormConfigTab
          expiresAt={expiresAt}
          onExpiresAtChange={setExpiresAt}
          expiresAtError={expiresAtError}
          isGraded={isGraded}
          showResultsAfterSubmit={showResultsAfterSubmit}
          onShowResultsAfterSubmitChange={setShowResultsAfterSubmit}
        />
      );
      break;
    default:
      break;
  }
  return (
    <BasePage>
      {/* Sticky top bar */}
      <header className="sticky top-0 z-10 border-b border-gray-200 bg-white px-6 py-2 shadow-sm flex items-center justify-between">
        <div>
          <input
            value={title}
            maxLength={255}
            onChange={(e) => {
              setTitle(e.target.value);
              setTitleError("");
            }}
            className={
              "max-w-xs truncate text-base font-semibold text-gray-900 bg-transparent " +
              "border border-transparent rounded px-1 -mx-1 outline-none " +
              "hover:border-gray-200 focus:border-brand-400 focus:ring-1 focus:ring-brand-400 " +
              (titleError ? "border-red-400" : "")
            }
          />
          {titleError && <p className="text-xs text-red-500 px-1">{titleError}</p>}
          <p className="text-xs text-gray-400">
            {questions.length} question{questions.length !== 1 ? "s" : ""}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Button variant="outline" onClick={() => navigate("/dashboard")}>
            Back
          </Button>
          <Button
            onClick={handleSave}
            isLoading={state === "saving"}
            disabled={state === "deleting"}
          >
            Save
          </Button>
          <Button
            variant="danger"
            onClick={openDeleteModal}
            disabled={state === "saving" || state === "deleting"}
          >
            Delete
          </Button>
        </div>
      </header>

      {/* Editor area */}
      <main className="mx-auto w-full max-w-2xl px-4 py-8 flex flex-col gap-3">
        <Tabs tabs={TABS} activeTab={activeTab} onTabChange={changeTab} />
        {currentTab}
      </main>

      {showDeleteModal && (
        <DeleteFormModal
          formTitle={form.title}
          questionCount={questions.length}
          submissionCount={submissionCount}
          isDeleting={state === "deleting"}
          onConfirm={handleDelete}
          onClose={() => setShowDeleteModal(false)}
        />
      )}
    </BasePage>
  );
}
