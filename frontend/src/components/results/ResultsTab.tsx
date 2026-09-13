import { useState } from "react";
import { Tabs, type TabDefinition } from "../Tabs";
import { SummaryResultsTab } from "./SummaryResultsTab";
import { IndividualResultsTab } from "./IndividualResultsTab";

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

  return (
    <div>
      <Tabs tabs={TABS} activeTab={activeTab} onTabChange={setActiveTab} size="sm" centered={false} />
      {
        (activeTab === 'summary' ?
          <SummaryResultsTab formId={formId} reloadKey={reloadKey} /> :
          <IndividualResultsTab formId={formId} reloadKey={reloadKey} />)
      }
    </div>);
}