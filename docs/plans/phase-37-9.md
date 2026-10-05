# Slice 9: `POST /api/forms/generate` as multipart, text only

Covers EP-1 and EP-2 of [`phase-37.md`](./phase-37.md). No file is accepted yet (slices 10-14 add it); this slice only changes the **route** and the **wire format**, so the later slices add a field instead of reworking the endpoint.

## Decisions

| Topic | Decision |
|---|---|
| Route | `POST /api/forms/generate` replaces `POST /api/forms/generate/text`. The old route is removed, not kept as an alias (the only caller is this frontend). |
| Wire format | `multipart/form-data` only (`[Consumes]`). A JSON body now answers 415. |
| Binding type | A new API-layer class, `GenerateFormForm`, bound with `[FromForm]` and mapped to the existing `GenerateFormRequest`. `Application` stays free of ASP.NET types, and the later `IFormFile` is turned into bytes in the controller, never passed down. `GenerateFormRequest` and the handler **do not change**. |
| `ExpiresAt` | Bound as `DateTimeOffset` and converted with `.UtcDateTime`. See the trap below. |
| Size limit | `[RequestSizeLimit]` on this action only, 11 MB (EP-2). It is ahead of the file slices on purpose. |
| Unchanged | Rate limit policy (`generate`), `CreatedAtAction` 201, handler behavior, `SourceText` 30,000 cap, error body shape. |

**Trap: `DateTime` from a form is not UTC.** JSON binding keeps `"...Z"` as `DateTimeKind.Utc`. The form model binder converts it to **local time** (`Kind = Local`). Npgsql refuses to write a non-UTC `DateTime` to `timestamptz`, and `request.ExpiresAt <= DateTime.UtcNow` in `GenerateFormHandler.ValidateForm` compares ticks ignoring `Kind`, so on a non-UTC server a past expiry could pass. Binding to `DateTimeOffset` and converting in the controller avoids both.

**Trap: axios turns `FormData` back into JSON.** `src/api/axios.ts` sets a default `Content-Type: application/json`, and axios 1.x serializes `FormData` to JSON when it sees that header. The request must override it with `multipart/form-data` (axios then lets the browser add the boundary).

---

## 1. Backend

### 1.1 New file `src/FormAI.API/Contracts/Forms/GenerateFormForm.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.API.Contracts.Forms;

// The multipart body of POST /api/forms/generate. It lives in the API layer because it is
// a transport shape; GenerateFormRequest is what the use case receives.
public class GenerateFormForm
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string SourceText { get; init; } = string.Empty;
    public int QuestionCount { get; init; }
    public QuestionType[]? AllowedTypes { get; init; }
    public DifficultyLevel DifficultyLevel { get; init; }
    public bool IsGraded { get; init; }
    public bool ShowResultsAfterSubmit { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
```

Notes:

- `SourceText` defaults to `string.Empty` (not `required`), so a missing field reaches `GenerateFormHandler` and gets its own `sourceText` message instead of the generic `ProblemDetails` 400 from `[ApiController]`.
- Do **not** put `SourceType` or `SourceUrl` in the form. The handler already hardcodes `SourceType.Text` and ignores the URL, so the controller passes `SourceType.Text` and `null`.
- Form binding matches names case-insensitively, so the frontend can keep sending camelCase.
- Enums bind by name or number (`"Medium"`). `JsonStringEnumConverter` does not apply to forms, and it is not needed.

### 1.2 `src/FormAI.API/Controllers/FormsController.cs`

Add a constant near the top of the class:

```csharp
// Room for the 10 MB file the later slices accept, plus the form fields and multipart framing.
private const long GenerateMaxRequestBytes = 11 * 1024 * 1024;
```

Replace the action (currently lines 109-117):

```csharp
// POST /api/forms/generate
[HttpPost("generate")]
[EnableRateLimiting(RateLimitPolicies.Generate)]
[Consumes("multipart/form-data")]
[RequestSizeLimit(GenerateMaxRequestBytes)]
public async Task<IActionResult> Generate([FromForm] GenerateFormForm form,
    CancellationToken cancellationToken)
{
    var request = new GenerateFormRequest(
        form.Title,
        form.Description,
        form.SourceText,
        SourceType.Text,
        null,
        form.QuestionCount,
        form.AllowedTypes,
        form.DifficultyLevel,
        form.IsGraded,
        form.ShowResultsAfterSubmit,
        form.ExpiresAt.UtcDateTime);

    var response = await _generateForm.HandleAsync(request, CurrentUserId, cancellationToken);
    return CreatedAtAction(nameof(GetById), new { id = response.FormId }, response);
}
```

Add `using FormAI.API.Contracts.Forms;` and `using FormAI.Domain.Enums;` to the controller.

Also update the comment block around lines 166-168 (`// POST /api/forms/generate/file`, `/url`, `/image`): those planned routes no longer fit. Replace them with a single note that file input arrives as a field on `POST /api/forms/generate`, or delete them.

### 1.3 Things to check, no change expected

- `UseRateLimiter()` stays after `UseAuthentication()`/`UseAuthorization()` in `Program.cs`. Nothing to do.
- The attribute is on the action, so no other endpoint gets the 11 MB limit (EP-2). Kestrel's 30 MB default still applies elsewhere.
- A body over 11 MB is rejected by Kestrel with **413** before the handler runs, so it is **not** in the `{ message, errors, code }` shape and does not use a rate-limit permit differently from today. That is acceptable for this slice; slice 13/15 handles the `SourceFileTooLarge` message from the file size check.
- `RateLimiting:Generate` wording in `RateLimitingExtensions.cs` does not mention the route. No change.

---

## 2. Frontend

### 2.1 `frontend/src/api/forms.ts`: replace `generateForm` (lines 62-77)

```ts
export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
  const body = new FormData();
  const title = payload.title?.trim();
  const description = payload.description?.trim();

  if (title) body.append("title", title);
  if (description) body.append("description", description);
  body.append("sourceText", payload.sourceText);
  body.append("questionCount", String(payload.questionCount));
  body.append("difficultyLevel", payload.difficultyLevel);
  body.append("isGraded", String(payload.isGraded));
  body.append("showResultsAfterSubmit", String(payload.showResultsAfterSubmit));
  body.append("expiresAt", payload.expiresAt.toISOString());

  // The instance default is application/json, and axios would serialize FormData to JSON with it.
  const response = await api.post<GenerateFormResult>("/forms/generate", body, {
    headers: { "Content-Type": "multipart/form-data" },
  });
  return response.data;
}
```

- `allowedTypes` was always `null`, so it is simply not sent. `sourceType` and `sourceUrl` are gone (the server owns them).
- Booleans are sent as `"true"`/`"false"`, which ASP.NET binds to `bool`.
- Slice 15 adds `file` and the upload-progress callback here.
- `GenerateFormPayload` and `CreateFormPage` do not change.

### 2.2 `frontend/src/pages/CreateFormPage.test.tsx`

- Line 9: `const GENERATE_URL = "*/api/forms/generate";`
- In the `it.each` block (lines 58-78), the request is no longer JSON. Replace the capture:

```ts
let requestBody: FormData | undefined;
server.use(
  http.post(GENERATE_URL, async ({ request }) => {
    requestBody = await request.formData();
    return HttpResponse.json({ formId: "form-1", title: "Generated" }, { status: 201 });
  }),
);
```

and the assertions:

```ts
await waitFor(() => expect(requestBody).toBeDefined());
expect(requestBody?.get("showResultsAfterSubmit")).toBe(String(expected));
expect(requestBody?.get("isGraded")).toBe(String(graded && !ungradeAfter));
```

- Check the rest of the file (below line 80) for other uses of `request.json()` against `GENERATE_URL` and convert them the same way.
- **Risk:** parsing multipart with `request.formData()` under jsdom + MSW can fail on some Node/jsdom combinations. If it does, fall back to `const text = await request.text()` and assert with `toContain('name="isGraded"')` plus the value, rather than fighting the environment.
- Add one test that pins the content type, since that is the trap above:

```ts
it("sends the form as multipart, not JSON", async () => {
  let contentType: string | null = null;
  server.use(
    http.post(GENERATE_URL, ({ request }) => {
      contentType = request.headers.get("content-type");
      return HttpResponse.json({ formId: "form-1", title: "Generated" }, { status: 201 });
    }),
  );

  const user = userEvent.setup();
  renderCreate();
  await user.type(screen.getByLabelText("Source content"), SOURCE_TEXT);
  await user.click(screen.getByRole("button", { name: "Generate form" }));

  await waitFor(() => expect(contentType).not.toBeNull());
  expect(contentType).toMatch(/^multipart\/form-data; boundary=/);
});
```

---

## 3. e2e: `frontend/e2e/generate-form.spec.ts`

Playwright's `multipart` option does not take arrays, so `allowedTypes` is dropped. It only shapes the prompt, and the fake gateway returns the same canned draft regardless.

Replace the request (lines 10-25):

```ts
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
```

Keep the existing assertions, and add a check that the expiry survived the form binding (guards the `Kind = Local` trap). `GetFormResponse` exposes `ExpiresAt`:

```ts
const stored = await request.get(`${API_URL}/api/forms/${form.formId}`, { headers: auth });
expect(stored.status()).toBe(200);
expect(new Date((await stored.json()).expiresAt).getTime()).toBe(expiresAt.getTime());
```

If the API stores or returns the value with a different precision (it sends `...:00.123Z`), compare to the second instead: `Math.floor(x / 1000)`.

Add two small route-contract tests in the same file:

```ts
test("the old generate/text route is gone", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));
  const res = await request.post(`${API_URL}/api/forms/generate/text`, {
    headers: auth,
    data: { sourceText: "x" },
  });
  expect(res.status()).toBe(404);
});

test("generate rejects a JSON body", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));
  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    data: { sourceText: "x" },
  });
  expect(res.status()).toBe(415);
});
```

Both fail the request before the handler, so neither reaches the gateway. The 415 comes from `[Consumes]` and the rate limiter runs first, so these spend a permit each; use a fresh user per test as above so the other generation tests are not starved.

Search the rest of `frontend/e2e/` for other callers (the earlier grep found only this spec) before finishing.

---

## 4. Docs

Same change, per the "Keeping the docs true" rules.

- **`CLAUDE.md`**, Business rules, the rate-limit bullet: `POST /api/forms/generate/text` becomes `POST /api/forms/generate`. In the same bullet, or the next, add one sentence: the endpoint takes `multipart/form-data` (a JSON body answers 415), is bound through `GenerateFormForm` in the API layer, `ExpiresAt` is bound as `DateTimeOffset` and converted to UTC, and the request size limit is set on this action only (11 MB). Do not describe a file field; it does not exist yet.
- **`docs/known-gaps.md`**:
  - Line 12 (*Generation from PDF, Word, image or URL*): `Only POST /api/forms/generate/text exists` becomes `Only POST /api/forms/generate exists, and it accepts text only`.
  - Line 21 (*Generation rate limit holds for one server only*): update the route.
  - Line 5 is a dated history sentence (`on 2026-09-25 POST /api/forms/generate/text is now rate limited ...`). Leave the date and wording as history, or update only the route; do not reword the fact.
- **`.specs/features/**`** and **`docs/plans/phase-37.md`** mention the old route as history. Leave them.
- No new term, so no `CONTEXT.md` change. No ADR: the decision is reversible and the alternative (a second route per input kind) is already ruled out by EP-1.

---

## 5. Verify

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj   # handler unchanged, must stay green
cd frontend
npm test
npm run test:e2e -- generate-form
```

Manually, with `dotnet run --project src/FormAI.API` and `npm run dev`:

1. Create a form from the UI. It lands on the editor, and the browser's Network tab shows `POST /api/forms/generate` with `Content-Type: multipart/form-data; boundary=...`.
2. In Swagger, confirm the action shows a form body and not JSON.
3. Create a form with the machine's timezone not UTC (your local one is fine) and check in the editor that the expiry is the one you picked.
4. Over-limit: send 11 attempts in an hour and confirm the 429 body is unchanged.

## Done when

- `POST /api/forms/generate` creates a form from multipart text; `POST /api/forms/generate/text` answers 404, and a JSON body to the new route answers 415.
- The stored `ExpiresAt` equals what the owner chose.
- Vitest, the unit tests and the generate e2e spec pass.
- `CLAUDE.md` and `docs/known-gaps.md` name the new route.

## Suggested commits

1. `feat(api): accept generate as multipart on POST /api/forms/generate`
2. `refactor(frontend): send the generate request as multipart`
3. `test(e2e): follow the generate route and pin the multipart contract`
4. `docs: rename the generate route in CLAUDE.md and known-gaps`

(Commits 1 and 2 break the app apart, so merge them in one PR; the split is for review only.)
