# Phase 4 — Drag-and-Drop Question Ordering

## Goal

Replace the ▲ / ▼ arrow buttons on the FormEditorPage with draggable question cards. Users will grab a card by a drag handle and drop it in the desired position. The arrows are removed entirely.

---

## 1. Install dependencies

```bash
cd frontend
npm install @dnd-kit/core @dnd-kit/sortable @dnd-kit/utilities
```

---

## 2. `frontend/src/pages/FormEditorPage.tsx`

- **Remove** the `moveQuestion(index, direction)` function.
- **Add** imports and a `handleDragEnd` function:

```ts
import { DndContext, closestCenter } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';

function handleDragEnd(event: DragEndEvent) {
  const { active, over } = event;
  if (!over || active.id === over.id) return;
  setQuestions(qs => {
    const oldIndex = qs.findIndex(q => q.id === active.id);
    const newIndex = qs.findIndex(q => q.id === over.id);
    return arrayMove(qs, oldIndex, newIndex);
  });
}
```

- **Wrap** the question list in `<DndContext>` + `<SortableContext>` and remove `onMoveUp`/`onMoveDown` props:

```tsx
<DndContext collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
  <SortableContext items={questions.map(q => q.id)} strategy={verticalListSortingStrategy}>
    {questions.map((q, i) => (
      <QuestionCard
        key={q.id}
        question={q}
        index={i}
        onChange={updated => updateQuestion(i, updated)}
        onRemove={() => removeQuestion(i)}
      />
    ))}
  </SortableContext>
</DndContext>
```

---

## 3. `frontend/src/components/editors/QuestionCard.tsx`

- **Remove** `onMoveUp`, `onMoveDown`, and `total` from `QuestionCardProps` and the destructure.
- **Add** `useSortable` hook and apply it to the card root:

```ts
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';

const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
  useSortable({ id: question.id });

const style = {
  transform: CSS.Transform.toString(transform),
  transition,
  opacity: isDragging ? 0.5 : 1,
};
```

- **Apply** `ref={setNodeRef}`, `style={style}`, and `{...attributes}` to the root `<div>`:

```tsx
<div ref={setNodeRef} style={style} {...attributes}
  className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
```

- **Replace** the ▲ ▼ button block with a drag handle that receives `{...listeners}`:

```tsx
{/* Reorder + delete column */}
<div className="flex flex-col items-center gap-2 shrink-0">
  <div
    {...listeners}
    className="cursor-grab active:cursor-grabbing p-1 text-gray-400 hover:text-gray-600"
    title="Drag to reorder"
  >
    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 256 256">
      <path d="M108,60A16,16,0,1,1,92,44,16,16,0,0,1,108,60Zm56,0a16,16,0,1,0-16-16A16,16,0,0,0,164,60ZM92,112a16,16,0,1,0,16,16A16,16,0,0,0,92,112Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,112ZM92,180a16,16,0,1,0,16,16A16,16,0,0,0,92,180Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,180Z"/>
    </svg>
  </div>
  <button
    onClick={onRemove}
    title="Delete question"
    className="rounded p-1 text-red-400 hover:text-red-600"
  >
    🗑
  </button>
</div>
```

---

## Verification

1. `npm run dev` inside `frontend/` — open the form editor for any generated form.
2. Confirm the ▲ ▼ arrows are gone; a grip icon appears instead.
3. Drag a question card to a new position — order updates in real time.
4. Click **Save** — confirm the reordered questions persist (the `updateQuestions` call sends the new array order).
5. Confirm edit, delete, and add-option actions still work after dragging.
