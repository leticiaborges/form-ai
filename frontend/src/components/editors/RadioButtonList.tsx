import type { FormOption } from '../../types/form';
import { OptionList } from './OptionsList';

interface RadioButtonListProps {
  options: FormOption[];
  questionId: string;
  onOptionsChange: (options: FormOption[]) => void;
}

export function RadioButtonList(props: RadioButtonListProps) {
  return <OptionList {...props} inputType="radio" />;
}