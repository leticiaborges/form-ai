import { ConfirmModal } from "../ConfirmModal";

interface DeleteFormModalProps {
  formTitle: string;
  questionCount: number;
  submissionCount: number | null;
  isDeleting: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

export function DeleteFormModal({
  formTitle,
  questionCount,
  submissionCount,
  isDeleting,
  onConfirm,
  onClose,
}: Readonly<DeleteFormModalProps>) {
  return (
    <ConfirmModal
      title="Delete this form?"
      confirmLabel="Delete form"
      variant="danger"
      isLoading={isDeleting}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <p>
        <span className="font-medium text-gray-900">“{formTitle}”</span> and everything attached to
        it will be permanently deleted:
      </p>

      <ul className="mt-3 list-disc space-y-1 pl-5">
        <li>{questionCount} question(s) and their options</li>
        {submissionCount != null && submissionCount > 0 && (
          <li>{`${submissionCount} submission(s) and all answers`}</li>
        )}
      </ul>

      <p className="mt-3 font-medium text-red-600">This cannot be undone.</p>
    </ConfirmModal>
  );
}
