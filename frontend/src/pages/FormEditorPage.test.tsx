import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { FormEditorPage } from "./FormEditorPage";
import type { FormDetail } from "../types/form";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";

const FORM_URL = "*/api/forms/form-1";
const EDITOR_URL = "*/api/forms/form-1/editor";
const SCORE_LABEL = "Show score after submit";

function storedForm(overrides: Partial<FormDetail> = {}): FormDetail {
  return {
    id: "form-1",
    title: "Capitals",
    description: "",
    isPublic: false,
    expiresAt: "2099-01-01T12:00:00Z",
    showResultsAfterSubmit: true,
    isGraded: true,
    createdAt: "2026-01-01T12:00:00Z",
    questions: [],
    ...overrides,
  };
}

// Serves the form and records every editor save, the way the real API would: the flag it stores
// is the one it read back on the next GET.
function serveForm(initial: FormDetail) {
  const saves: Record<string, unknown>[] = [];
  let stored = initial;

  server.use(
    http.get(FORM_URL, () => HttpResponse.json(stored)),
    http.put(EDITOR_URL, async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      saves.push(body);
      stored = {
        ...stored,
        isGraded: body.isGraded as boolean,
        showResultsAfterSubmit: body.showResultsAfterSubmit as boolean,
      };
      return new HttpResponse(null, { status: 204 });
    }),
  );

  return { saves };
}

function renderEditor() {
  return renderWithProviders(<FormEditorPage />, {
    route: "/forms/form-1/edit",
    path: "/forms/:id/edit",
  });
}

async function openTab(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(await screen.findByRole("tab", { name }));
}

function scoreCheckbox() {
  return screen.queryByRole("checkbox", { name: SCORE_LABEL });
}

describe("FormEditorPage", () => {
  it("reflects the stored flag on the Configuration tab after load and after save", async () => {
    const { saves } = serveForm(storedForm({ showResultsAfterSubmit: true }));
    const user = userEvent.setup();
    renderEditor();

    await openTab(user, "Configuration");
    expect(scoreCheckbox()).toBeChecked();

    // The API answers the next GET with the flag off, whatever this screen held.
    server.use(
      http.get(FORM_URL, () => HttpResponse.json(storedForm({ showResultsAfterSubmit: false }))),
    );

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    await waitFor(() => expect(scoreCheckbox()).not.toBeChecked());
  });

  it("sends showResultsAfterSubmit false after Graded form is unticked", async () => {
    const { saves } = serveForm(storedForm({ showResultsAfterSubmit: true }));
    const user = userEvent.setup();
    renderEditor();

    await user.click(await screen.findByRole("checkbox", { name: "Graded form" }));

    await openTab(user, "Configuration");
    expect(scoreCheckbox()).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].isGraded).toBe(false);
    expect(saves[0].showResultsAfterSubmit).toBe(false);
  });

  it("keeps the flag off when Graded form is ticked again", async () => {
    const { saves } = serveForm(storedForm({ showResultsAfterSubmit: true }));
    const user = userEvent.setup();
    renderEditor();

    const graded = await screen.findByRole("checkbox", { name: "Graded form" });
    await user.click(graded);
    await user.click(graded);

    await openTab(user, "Configuration");
    expect(scoreCheckbox()).not.toBeChecked();

    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].isGraded).toBe(true);
    expect(saves[0].showResultsAfterSubmit).toBe(false);
  });
});
