interface IndividualResultsTabProps {
  formId: string;
  reloadKey: number;
}

export function IndividualResultsTab({ formId, reloadKey }: Readonly<IndividualResultsTabProps>) {
  return (<div>Individual results {formId}. ReloadKey: {reloadKey}</div>);
}