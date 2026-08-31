export interface TabDefinition {
  id: string;
  label: string;
}

interface TabsProps {
  tabs: TabDefinition[];
  activeTab: string;
  onTabChange: (id: string) => void;
}

export function Tabs({
  tabs, activeTab, onTabChange
}: Readonly<TabsProps>) {
  return (
    <div role="tablist" className="flex items-center gap-1 border-b border-gray-200">
      {tabs.map(tab => {
        const isActive = tab.id === activeTab;

        return (
          <button
            key={tab.id}
            role="tab"
            type="button"
            aria-selected={isActive}
            onClick={() => onTabChange(tab.id)}
            className={
              'px-4 py-2 text-sm font-medium transition-colors border-b-2 -mb-px ' +
              (isActive
                ? 'border-brand-600 text-brand-700'
                : 'border-transparent text-gray-500 hover:text-gray-800 hover:border-gray-300')
            }
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}