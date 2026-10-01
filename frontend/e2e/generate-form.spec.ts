import { test, expect } from "@playwright/test";
import { registerVerifiedUser, signIn } from "./support/api";
import { API_URL } from "./env";

test("generating a form goes through the gateway and saves the draft it returns", async ({
  request,
}) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate/text`, {
    headers: auth,
    data: {
      title: "Generated through the fake gateway",
      description: "",
      sourceText: "Paris is the capital of France. The why: is just because.",
      sourceType: "Text",
      sourceUrl: "",
      questionCount: 2,
      allowedTypes: ["Single", "Text"],
      difficultyLevel: "Medium",
      isGraded: true,
      showResultsAfterSubmit: false,
      expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
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
});
