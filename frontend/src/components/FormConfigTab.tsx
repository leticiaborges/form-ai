import { DateTimeInput } from "./DateTimeInput";

interface FormConfigTabProps {
  expiresAt: string;
  onExpiresAtChange: (value: string) => void;
  expiresAtError: string;
}

export function FormConfigTab({ expiresAt, onExpiresAtChange, expiresAtError }: Readonly<FormConfigTabProps>) {


  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <DateTimeInput id="expiresAt"
          label="Expires at"
          value={expiresAt}
          onChange={onExpiresAtChange}
          error={expiresAtError}
          timeFormat="HH:mm" />
      </div>
    </div>);
}