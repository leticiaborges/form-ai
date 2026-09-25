import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FormConfigTab } from "./FormConfigTab";

const SCORE_LABEL = "Show score after submit";

function renderTab(
  props: { isGraded: boolean; showResultsAfterSubmit: boolean },
  onChange = vi.fn(),
) {
  return render(
    <FormConfigTab
      expiresAt="2099-01-01T10:00"
      onExpiresAtChange={() => {}}
      expiresAtError=""
      onShowResultsAfterSubmitChange={onChange}
      {...props}
    />,
  );
}

describe("FormConfigTab", () => {
  it("shows the score checkbox reflecting its value when the form is graded", () => {
    const { rerender } = renderTab({ isGraded: true, showResultsAfterSubmit: true });
    expect(screen.getByRole("checkbox", { name: SCORE_LABEL })).toBeChecked();

    rerender(
      <FormConfigTab
        expiresAt="2099-01-01T10:00"
        onExpiresAtChange={() => {}}
        expiresAtError=""
        isGraded
        showResultsAfterSubmit={false}
        onShowResultsAfterSubmitChange={() => {}}
      />,
    );
    expect(screen.getByRole("checkbox", { name: SCORE_LABEL })).not.toBeChecked();
  });

  it("hides the score checkbox when the form is ungraded", () => {
    renderTab({ isGraded: false, showResultsAfterSubmit: true });

    expect(screen.queryByRole("checkbox", { name: SCORE_LABEL })).not.toBeInTheDocument();
  });

  it("reports the new value when the score checkbox is clicked", async () => {
    const onChange = vi.fn();
    renderTab({ isGraded: true, showResultsAfterSubmit: false }, onChange);

    await userEvent.setup().click(screen.getByRole("checkbox", { name: SCORE_LABEL }));

    expect(onChange).toHaveBeenCalledWith(true);
  });
});
