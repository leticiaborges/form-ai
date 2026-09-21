import type { FormOption } from "../../types/form";
import { OptionList } from "./OptionsList";

interface RadioButtonListProps {
  options: FormOption[];
  questionId: string;
  isGraded: boolean;
  onOptionsChange: (options: FormOption[]) => void;
}

export function RadioButtonList(props: Readonly<RadioButtonListProps>) {
  return <OptionList {...props} inputType="radio" />;
}
