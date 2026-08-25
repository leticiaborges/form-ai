import type { DraggableSyntheticListeners } from "@dnd-kit/core";


export const DRAG_RAIL_CLASSES =
    'flex shrink-0 items-center justify-center border-l border-gray-200 ' +
    'bg-gray-50 text-gray-400 cursor-grab active:cursor-grabbing hover:bg-gray-100 hover:text-gray-600';

interface DragHandleRailProps {
    /** `listeners` from useSortable — attaches the drag activator to the rail. */
    listeners: DraggableSyntheticListeners;
    /** Narrower rail + smaller grip, for option rows. */
    compact?: boolean;
    title?: string;
}

export function DragHandleRail({
    listeners,
    compact = false,
    title = 'Drag to reorder'
}: DragHandleRailProps) {
    const size = compact ? 16 : 20;

    return (
        <div
            {...listeners}
            style={{ touchAction: 'none' }}
            title={title}
            className={`${DRAG_RAIL_CLASSES} ${compact ? 'w-8' : 'w-10'}`}
        >
            <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} fill="currentColor" viewBox="0 0 256 256">
                <path d="M108,60A16,16,0,1,1,92,44,16,16,0,0,1,108,60Zm56,0a16,16,0,1,0-16-16A16,16,0,0,0,164,60ZM92,112a16,16,0,1,0,16,16A16,16,0,0,0,92,112Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,112ZM92,180a16,16,0,1,0,16,16A16,16,0,0,0,92,180Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,180Z" />
            </svg>
        </div>
    );
}