import { test, expect } from "@playwright/test";
import { seedPublishedGradedForm } from "./support/api";

test("a published form collects an anonymous submission that the owner sees live", async ({
  browser,
  request,
}) => {
  const form = await seedPublishedGradedForm(request);

  const ownerContext = await browser.newContext();
  const respondentContext = await browser.newContext();

  try {
    const owner = await ownerContext.newPage();
    const respondent = await respondentContext.newPage();

    await test.step("owner opens the Results tab of the published form", async () => {
      await owner.goto("/login");
      await owner.getByLabel("Email").fill(form.owner.email);
      await owner.getByLabel("Password").fill(form.owner.password);
      await owner.getByRole("button", { name: "Log in" }).click();
      // Wait for the login to finish (the app navigates to the dashboard once the tokens are stored);
      // navigating earlier aborts the request and leaves the owner signed out.
      await expect(owner).toHaveURL(/\/dashboard/);
      await owner.goto(`/forms/${form.formId}/edit?tab=results`);
      await expect(owner.getByText("No submissions yet.")).toBeVisible();
    });

    await test.step("respondent cannot submit without the required answer", async () => {
      await respondent.goto(`/forms/${form.formId}/answer`);
      await expect(respondent.getByText(form.title)).toBeVisible();
      await respondent.getByRole("button", { name: "Submit" }).click();
      await expect(respondent.getByText("This question is required")).toBeVisible();
    });

    await test.step("respondent submits the correct answer", async () => {
      await respondent.getByRole("radio", { name: form.question.correct }).check();
      await respondent.getByRole("button", { name: "Submit" }).click();
      await expect(respondent.getByText("Thanks! Your response has been recorded.")).toBeVisible();
    });

    await test.step("respondent cannot submit twice", async () => {
      await respondent.reload();
      await expect(
        respondent.getByText("You've already responded to this form. Thanks!"),
      ).toBeVisible();
    });

    await test.step("owner sees the submission without reloading", async () => {
      await expect(owner.getByText("1 submission")).toBeVisible();
    });
  } finally {
    await ownerContext.close();
    await respondentContext.close();
  }
});
