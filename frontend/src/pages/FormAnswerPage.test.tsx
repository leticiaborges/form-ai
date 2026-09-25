import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { FormAnswerPage } from "./FormAnswerPage";
import type { AnswerForm } from "../types/submission";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";

const THANKS = "Thanks! Your response has been recorded.";

const answerForm: AnswerForm = {
  id: "form-1",
  title: "Capitals",
  description: null,
  questions: [
    {
      id: "q1",
      text: "Capital of France?",
      type: "Single",
      order: 1,
      isRequired: true,
      options: [
        { id: "o1", text: "Paris", order: 1 },
        { id: "o2", text: "Lyon", order: 2 },
      ],
    },
  ],
};

async function answerAndSubmit(submitBody: object) {
  server.use(
    http.get("*/api/forms/form-1/my-submission", () =>
      HttpResponse.json({ hasSubmitted: false, submittedAt: null }),
    ),
    http.get("*/api/forms/form-1/answer", () => HttpResponse.json(answerForm)),
    http.post("*/api/forms/form-1/submit", () => HttpResponse.json(submitBody)),
  );

  const user = userEvent.setup();
  renderWithProviders(<FormAnswerPage />, {
    route: "/forms/form-1/answer",
    path: "/forms/:id/answer",
  });

  await user.click(await screen.findByRole("radio", { name: "Paris" }));
  await user.click(screen.getByRole("button", { name: "Submit" }));
}

describe("FormAnswerPage", () => {
  it("shows the score out of the maximum after submitting", async () => {
    await answerAndSubmit({ id: "submission-1", totalScore: 6, maxScore: 10 });

    expect(await screen.findByText(THANKS)).toBeInTheDocument();
    expect(screen.getByText("6/10")).toBeInTheDocument();
  });

  it.each([
    { name: "both null", totalScore: null, maxScore: null },
    { name: "score null", totalScore: null, maxScore: 10 },
    { name: "maximum null", totalScore: 6, maxScore: null },
  ])(
    "shows only the thanks message when the score or the maximum is null ($name)",
    async ({ totalScore, maxScore }) => {
      await answerAndSubmit({ id: "submission-1", totalScore, maxScore });

      expect(await screen.findByText(THANKS)).toBeInTheDocument();
      expect(screen.queryByText(/\d\s*\/\s*\d/)).not.toBeInTheDocument();
    },
  );
});
