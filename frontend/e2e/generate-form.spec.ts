import { test, expect } from "@playwright/test";
import { registerVerifiedUser, signIn } from "./support/api";
import { API_URL } from "./env";

test("generating a form goes through the gateway and saves the draft it returns", async ({
  request,
}) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);
  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      title: "Generated through the fake gateway",
      sourceText: "Paris is the capital of France. The why: is just because.",
      questionCount: 2,
      difficultyLevel: "Medium",
      isGraded: true,
      showResultsAfterSubmit: false,
      expiresAt: expiresAt.toISOString(),
    },
  });

  expect(res.status(), await res.text()).toBe(201);
  const form = await res.json();

  expect(form.questions.map((q: { text: string }) => q.text)).toEqual([
    "What is the capital of France?",
    "Why?",
  ]);

  expect(form.questions[0].options.map((o: { isCorrect: boolean }) => o.isCorrect)).toEqual([
    true,
    false,
    false,
  ]);
  expect(form.questions[1].correctAnswer).toBe("Because");

  const stored = await request.get(`${API_URL}/api/forms/${form.formId}`, { headers: auth });
  expect(stored.status()).toBe(200);
  expect(new Date((await stored.json()).expiresAt).getTime()).toBe(expiresAt.getTime());
});
