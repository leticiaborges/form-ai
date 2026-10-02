import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { CreateFormPage } from "./CreateFormPage";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";

const GENERATE_URL = "*/api/forms/generate";
const SCORE_LABEL = "Show score after submit";
const SOURCE_TEXT = "The quick brown fox jumps over the lazy dog, again and again and again.";

function renderCreate() {
  return renderWithProviders(<CreateFormPage />, { route: "/forms/new", path: "/forms/new" });
}

function scoreCheckbox() {
  return screen.queryByRole("checkbox", { name: SCORE_LABEL });
}

describe("CreateFormPage", () => {
  it("shows an unticked score checkbox once Graded form is ticked", async () => {
    const user = userEvent.setup();
    renderCreate();

    await user.click(screen.getByLabelText("Graded form"));

    expect(screen.getByRole("checkbox", { name: SCORE_LABEL })).not.toBeChecked();
  });

  it("hides the score checkbox while Graded form is unticked", async () => {
    const user = userEvent.setup();
    renderCreate();

    expect(scoreCheckbox()).not.toBeInTheDocument();

    await user.click(screen.getByLabelText("Graded form"));
    expect(scoreCheckbox()).toBeInTheDocument();

    await user.click(screen.getByLabelText("Graded form"));
    expect(scoreCheckbox()).not.toBeInTheDocument();
  });

  it.each([
    { name: "both ticked", graded: true, score: true, ungradeAfter: false, expected: true },
    { name: "graded only", graded: true, score: false, ungradeAfter: false, expected: false },
    {
      name: "score ticked, then graded unticked",
      graded: true,
      score: true,
      ungradeAfter: true,
      expected: false,
    },
    { name: "neither", graded: false, score: false, ungradeAfter: false, expected: false },
  ])(
    "sends showResultsAfterSubmit only when graded and score are both ticked ($name)",
    async ({ graded, score, ungradeAfter, expected }) => {
      let requestBody: FormData | undefined;
      server.use(
        http.post(GENERATE_URL, async ({ request }) => {
          requestBody = await request.formData();
          return HttpResponse.json({ formId: "form-1", title: "Generated" }, { status: 201 });
        }),
      );

      const user = userEvent.setup();
      renderCreate();

      await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
      if (graded) await user.click(screen.getByLabelText("Graded form"));
      if (score) await user.click(screen.getByRole("checkbox", { name: SCORE_LABEL }));
      if (ungradeAfter) await user.click(screen.getByLabelText("Graded form"));

      await user.click(screen.getByRole("button", { name: "Generate form" }));

      await waitFor(() => expect(requestBody).toBeDefined());
      expect(requestBody?.get("showResultsAfterSubmit")).toBe(String(expected));
      expect(requestBody?.get("isGraded")).toBe(String(graded && !ungradeAfter));
    },
  );
});
