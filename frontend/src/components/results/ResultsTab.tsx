import { useState } from "react";
import { Tabs, type TabDefinition } from "../Tabs";
import { SummaryResultsTab } from "./SummaryResultsTab";
import { IndividualResultsTab } from "./IndividualResultsTab";
import { useFormResultsHub } from "../../hooks/useFormResultsHub";

interface ResultsTabProps {
  formId: string;
  reloadKey: number;
}

const TABS: TabDefinition[] = [
  { id: 'summary', label: 'Summary' },
  { id: 'individual', label: 'Individual' }
];

export function ResultsTab({ formId, reloadKey }: Readonly<ResultsTabProps>) {

  const [activeTab, setActiveTab] = useState('summary');
  const [liveReloadKey, setLiveReloadKey] = useState(0);

  useFormResultsHub(formId, () => setLiveReloadKey((k) => k + 1));

  return (
    <div>
      <Tabs tabs={TABS} activeTab={activeTab} onTabChange={setActiveTab} size="sm" centered={false} />
      {
        (activeTab === 'summary' ?
          <SummaryResultsTab formId={formId} reloadKey={reloadKey + liveReloadKey} /> :
          <IndividualResultsTab formId={formId} reloadKey={reloadKey + liveReloadKey} />)
      }
    </div>);
}