import { closestCenter, DndContext, type DragEndEvent } from "@dnd-kit/core";
import type { FormOption } from "../../types/form";
import { arrayMove, SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { SortableOptionRow } from "./SortableOptionRow";

interface OptionListProps {
    options: FormOption[];
    questionId: string;
    inputType: 'checkbox' | 'radio'
    onOptionsChange: (options: FormOption[]) => void
}

export function OptionList({
    options, questionId, inputType, onOptionsChange
}: OptionListProps) {

    function updateOption(index: number,
        patch: Partial<FormOption>) {
        onOptionsChange(options.map((o, i) =>
            i == index ? { ...o, ...patch } : o));
    }

    function removeOption(index: number) {
        onOptionsChange(options.filter((_, i) => i !== index));
    }

    function addOption() {
        onOptionsChange([
            ...options,
            {
                id: crypto.randomUUID(),
                text: 'New option',
                order: options.length + 1,
                isCorrect: false
            }
        ])
    }

    function handleDragEnd(event: DragEndEvent) {
        const { active, over } = event;
        if (!over || active.id === over.id)
            return;

        const oldIndex = options.findIndex(o => o.id === active.id);
        const newIndex = options.findIndex(o => o.id === over.id);
        onOptionsChange(arrayMove(options, oldIndex, newIndex));
    }

    return (
        <div className="mt-3 space-y-1">
            <DndContext collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                <SortableContext items={options.map(o => o.id)} strategy={verticalListSortingStrategy}>
                    {options.map((opt, i) => (
                        <SortableOptionRow
                            key={opt.id}
                            option={opt}
                            inputType={inputType}
                            questionId={questionId}
                            onTextChange={(text) => updateOption(i, { text })}
                            onCorrectChange={(isCorrect) => updateOption(i, { isCorrect })}
                            onRemove={() => removeOption(i)}
                        />
                    ))}
                </SortableContext>
            </DndContext>

            <button
                onClick={addOption}
                className="mt-1 text-xs text-brand-600 hover:text-brand-800 hover:underline"
            >
                + Add option
            </button>
        </div>
    )
}