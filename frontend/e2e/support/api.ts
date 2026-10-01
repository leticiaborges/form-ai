import { randomUUID } from "node:crypto";
import { expect, type APIRequestContext, type APIResponse } from "@playwright/test";
import { API_URL, MAILPIT_URL, PASSWORD_FORTESTS } from "../env";

async function expectOk(res: APIResponse, action: string) {
  expect(res.ok(), `${action} failed: ${res.status()} ${await res.text()}`).toBeTruthy();
}

async function confirmEmail(request: APIRequestContext, email: string) {
  let token = "";
  await expect
    .poll(async () => {
      const search = await request.get(`${MAILPIT_URL}/api/v1/search`, {
        params: { query: `to:${email}` },
      });
      const { messages } = await search.json();
      if (!messages?.length) return "";

      const mail = await (
        await request.get(`${MAILPIT_URL}/api/v1/message/${messages[0].ID}`)
      ).json();
      token = /verify-email\?token=([^\s"<>]+)/.exec(mail.Text)?.[1] ?? "";
      return token;
    })
    .not.toBe("");

  const res = await request.post(`${API_URL}/api/auth/verify-email`, {
    data: { token: decodeURIComponent(token) },
  });
  await expectOk(res, "verify-email");
}

export interface SeededForm {
  formId: string;
  title: string;
  owner: { email: string; password: string };
  question: { id: string; text: string; correct: string; wrong: string };
}

export interface SeedOptions {
  isGraded?: boolean;
  showResultsAfterSubmit?: boolean;
}

export async function registerVerifiedUser(
  request: APIRequestContext,
): Promise<{ email: string; password: string }> {
  const email = `e2e-${randomUUID()}@example.com`;
  await expectOk(
    await request.post(`${API_URL}/api/auth/register`, {
      data: { name: "E2E Owner", email, password: PASSWORD_FORTESTS },
    }),
    "register",
  );
  await confirmEmail(request, email);
  return { email, password: PASSWORD_FORTESTS };
}

export async function signIn(
  request: APIRequestContext,
  { email, password }: { email: string; password: string },
): Promise<{ Authorization: string }> {
  const login = await request.post(`${API_URL}/api/auth/login`, { data: { email, password } });
  await expectOk(login, "login");
  const { accessToken } = await login.json();
  return { Authorization: `Bearer ${accessToken}` };
}

export async function seedPublishedGradedForm(
  request: APIRequestContext,
  { isGraded = true, showResultsAfterSubmit = false }: SeedOptions = {},
): Promise<SeededForm> {
  const { email } = await registerVerifiedUser(request);
  const login = await request.post(`${API_URL}/api/auth/login`, {
    data: { email, password: PASSWORD_FORTESTS },
  });
  await expectOk(login, "login");
  const { accessToken } = await login.json();
  const auth = { Authorization: `Bearer ${accessToken}` };

  const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
  const title = `E2E form ${randomUUID().slice(0, 8)}`;

  const created = await request.post(`${API_URL}/api/forms`, {
    headers: auth,
    data: {
      title,
      description: "",
      isPublic: false,
      expiresAt,
      showResultsAfterSubmit: false,
      isGraded: true,
    },
  });
  await expectOk(created, "create form");
  const { id: formId } = await created.json();

  // Option texts are unique within the question (QuestionOptionValidator) and double as the
  // radio labels the test clicks, so they are fixed strings rather than random ones.
  const question = {
    id: randomUUID(),
    text: "What is the capital of France?",
    correct: "Paris",
    wrong: "Lyon",
  };

  // What the editor would send: ids are client-generated, and the server keeps them (ADR 0002).
  // Publishing (isPublic) and grading (isGraded) are set here, so the form is answerable at once.
  const editorBody = {
    title,
    description: "",
    isPublic: true,
    isGraded,
    showResultsAfterSubmit,
    expiresAt,
    questions: [
      {
        id: question.id,
        text: question.text,
        type: "Single",
        order: 1,
        isRequired: true,
        aiGenerated: false,
        points: 2,
        correctAnswer: null,
        options: [
          { id: randomUUID(), text: question.correct, order: 1, isCorrect: true },
          { id: randomUUID(), text: question.wrong, order: 2, isCorrect: false },
          { id: randomUUID(), text: "Marseille", order: 3, isCorrect: false },
        ],
      },
      {
        id: randomUUID(),
        text: "Anything else to add?",
        type: "Text",
        order: 2,
        isRequired: false,
        aiGenerated: false,
        points: 0, // graded forms require points; 0 = part of the form but does not count
        correctAnswer: null,
        options: [],
      },
    ],
  };
  const saved = await request.put(`${API_URL}/api/forms/${formId}/editor`, {
    headers: auth,
    data: editorBody,
  });
  await expectOk(saved, "save form editor");

  return {
    formId,
    title,
    owner: { email, password: PASSWORD_FORTESTS },
    question,
  };
}
