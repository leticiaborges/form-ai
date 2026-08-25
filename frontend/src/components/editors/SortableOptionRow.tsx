import { useSortable } from "@dnd-kit/sortable";
import type { FormOption } from "../../types/form";
import { CSS } from '@dnd-kit/utilities';
import { OptionRow } from "./OptionRow";
import { DragHandleRail } from "./DragHandleRail";

interface SortableOptionsRowProps {
    option: FormOption;
    inputType: 'checkbox' | 'radio';
    questionId: string;
    onTextChange: (text: string) => void;
    onCorrectChange: (isCorrect: boolean) => void;
    onRemove: () => void;
}

export function SortableOptionRow({
    option,
    inputType,
    questionId,
    onTextChange,
    onCorrectChange,
    onRemove
}: SortableOptionsRowProps) {
    const { attributes, listeners, setNodeRef,
        transform, transition, isDragging
    } = useSortable({ id: option.id });

    const style = {
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1
    };

    return (
        <div ref={setNodeRef} style={style} {...attributes}
            className="flex items-stretch overflow-hidden rounded-md border border-gray-200 bg-white">

            <OptionRow
                text={option.text}
                isCorrect={option.isCorrect}
                inputType={inputType}
                questionId={questionId}
                onTextChange={onTextChange}
                onCorrectChange={onCorrectChange}
                onRemove={onRemove}
            />

            {/* Drag handle rail, attached to the row via a divider */}
            <DragHandleRail listeners={listeners} compact />
        </div>
    );
}