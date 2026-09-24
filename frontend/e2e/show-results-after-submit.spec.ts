import { randomUUID } from "node:crypto";
import { test, expect, type APIRequestContext } from "@playwright/test";
import { API_URL } from "./env";
import { seedPublishedGradedForm, type SeededForm } from "./support/api";

async function submitRightAnswer(request: APIRequestContext, form: SeededForm) {
  // The server gives a question it has not stored before an id of its own, so the ids to answer
  // with are the ones the respondent's own read of the form returns.
  const toAnswer = await (await request.get(`${API_URL}/api/forms/${form.formId}/answer`)).json();
  const question = toAnswer.questions.find((q: { text: string }) => q.text === form.question.text);
  const correct = question.options.find((o: { text: string }) => o.text === form.question.correct);

  const response = await request.post(`${API_URL}/api/forms/${form.formId}/submit`, {
    data: {
      respondentToken: randomUUID(),
      answers: [{ questionId: question.id, selectedOptionIds: [correct.id] }],
    },
  });
  return { status: response.status(), body: await response.json() };
}

test("editor save stores the flag only on a graded form", async ({ request }) => {
  const form = await seedPublishedGradedForm(request);
  const save = (settings: { isGraded: boolean; showResultsAfterSubmit: boolean }) =>
    request.put(`${API_URL}/api/forms/${form.formId}/editor`, {
      headers: form.auth,
      data: { ...form.editorBody, ...settings },
    });
  const read = async () =>
    (await request.get(`${API_URL}/api/forms/${form.formId}`, { headers: form.auth })).json();

  expect((await save({ isGraded: true, showResultsAfterSubmit: true })).status()).toBe(204);
  expect((await read()).showResultsAfterSubmit).toBe(true);

  expect((await save({ isGraded: false, showResultsAfterSubmit: true })).status()).toBe(204);
  const ungraded = await read();
  expect(ungraded.isGraded).toBe(false);
  expect(ungraded.showResultsAfterSubmit).toBe(false);
});

test("submit returns score and maximum only on a graded form with the flag set", async ({
  request,
}) => {
  await test.step("graded form with the flag set", async () => {
    const form = await seedPublishedGradedForm(request, { showResultsAfterSubmit: true });
    const { status, body } = await submitRightAnswer(request, form);

    expect(status, JSON.stringify(body)).toBe(200);
    expect(body).toEqual({ id: expect.any(String), totalScore: 2, maxScore: 2 });
  });

  await test.step("graded form with the flag off", async () => {
    const form = await seedPublishedGradedForm(request, { showResultsAfterSubmit: false });
    const { status, body } = await submitRightAnswer(request, form);

    expect(status, JSON.stringify(body)).toBe(200);
    expect(body).toEqual({ id: expect.any(String), totalScore: null, maxScore: null });
  });

  await test.step("ungraded form, even though the save asked for the flag", async () => {
    const form = await seedPublishedGradedForm(request, {
      isGraded: false,
      showResultsAfterSubmit: true,
    });
    const { status, body } = await submitRightAnswer(request, form);

    expect(status, JSON.stringify(body)).toBe(200);
    expect(body).toEqual({ id: expect.any(String), totalScore: null, maxScore: null });
  });
});

test("owner ticks the score checkbox and the respondent sees the score", async ({
  browser,
  request,
}) => {
  const form = await seedPublishedGradedForm(request);

  const ownerContext = await browser.newContext();
  const respondentContext = await browser.newContext();

  try {
    const owner = await ownerContext.newPage();
    const respondent = await respondentContext.newPage();
    const scoreCheckbox = owner.getByRole("checkbox", { name: "Show score after submit" });

    await test.step("owner ticks the checkbox on the Configuration tab and saves", async () => {
      await owner.goto("/login");
      await owner.getByLabel("Email").fill(form.owner.email);
      await owner.getByLabel("Password").fill(form.owner.password);
      await owner.getByRole("button", { name: "Log in" }).click();
      await expect(owner).toHaveURL(/\/dashboard/);

      await owner.goto(`/forms/${form.formId}/edit?tab=config`);
      await expect(scoreCheckbox).not.toBeChecked();
      await scoreCheckbox.check();
      await owner.getByRole("button", { name: "Save" }).click();
      await expect(owner.getByText("Form saved.")).toBeVisible();
    });

    await test.step("the flag is still ticked after reloading", async () => {
      await owner.reload();
      await expect(scoreCheckbox).toBeChecked();
    });

    await test.step("respondent submits the right answer and sees the score", async () => {
      await respondent.goto(`/forms/${form.formId}/answer`);
      await respondent.getByRole("radio", { name: form.question.correct }).check();
      await respondent.getByRole("button", { name: "Submit" }).click();

      await expect(respondent.getByText("Thanks! Your response has been recorded.")).toBeVisible();
      await expect(respondent.getByText("2/2")).toBeVisible();
    });
  } finally {
    await ownerContext.close();
    await respondentContext.close();
  }
});
