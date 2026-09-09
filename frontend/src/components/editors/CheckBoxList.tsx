import type { FormOption } from '../../types/form';
import { OptionList } from './OptionsList';

interface CheckBoxListProps {
  options: FormOption[];
  questionId: string;
  isGraded: boolean;
  onOptionsChange: (options: FormOption[]) => void;
}

export function CheckBoxList(props: Readonly<CheckBoxListProps>) {
  return <OptionList {...props} inputType="checkbox" />;
}