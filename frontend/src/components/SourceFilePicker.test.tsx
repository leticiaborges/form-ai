import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { SourceFilePicker } from "./SourceFilePicker";
import { MAX_FILE_BYTES } from "../utils/sourceFile";

function pdf(name = "report.pdf", size = 2048) {
  return new File([new Uint8Array(size)], name, { type: "application/pdf" });
}

function Harness({ onChange }: Readonly<{ onChange?: (f: File | null) => void }>) {
  const [file, setFile] = useState<File | null>(null);

  return (
    <SourceFilePicker
      id="file"
      file={file}
      onChange={(f) => {
        setFile(f);
        onChange?.(f);
      }}
    />
  );
}

describe("SourceFilePicker", () => {
  it("shows the name and size of the picked file", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("report.pdf", 2048));

    expect(screen.getByText("report.pdf")).toBeInTheDocument();
    expect(screen.getByText("2.0 KB")).toBeInTheDocument();
    expect(screen.getByText("Replace file")).toBeInTheDocument();
  });

  it("accepts one file only", () => {
    render(<Harness />);

    const input = screen.getByLabelText("Attach a file") as HTMLInputElement;
    expect(input.multiple).toBe(false);
    expect(input.accept).toBe(".pdf,.docx,.pptx,.txt");
  });

  it("removes the file", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf());
    await user.click(screen.getByRole("button", { name: "Remove report.pdf" }));

    expect(screen.queryByText("report.pdf")).not.toBeInTheDocument();
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("rejects an unsupported type and keeps the previous file", async () => {
    const user = userEvent.setup({ applyAccept: false });
    render(<Harness />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("keep.pdf"));

    await user.upload(
      screen.getByLabelText("Replace file"),
      new File(["x"], "photo.png", { type: "image/png" }),
    );

    expect(screen.getByRole("alert")).toHaveTextContent("isn't supported");
    expect(screen.getByText("keep.pdf")).toBeInTheDocument();
  });

  it("rejects a file over 10 MB", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("big.pdf", MAX_FILE_BYTES + 1));

    expect(screen.getByRole("alert")).toHaveTextContent("too large");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("clears the message once a valid file is picked", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.upload(screen.getByLabelText("Attach a file"), pdf("big.pdf", MAX_FILE_BYTES + 1));
    await user.upload(screen.getByLabelText("Attach a file"), pdf("ok.pdf"));

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows an error that comes from outside", () => {
    render(<SourceFilePicker id="file" file={null} onChange={vi.fn()} error="Required" />);

    expect(screen.getByRole("alert")).toHaveTextContent("Required");
  });
});
