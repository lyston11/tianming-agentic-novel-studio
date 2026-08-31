import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

export interface SettingsOption {
  value: string;
  label: string;
}

interface SettingsSelectProps {
  value: string;
  options: SettingsOption[];
  onChange?: (value: string) => void;
  disabled?: boolean;
  placeholder?: string;
}

export function SettingsSelect({ value, options, onChange, disabled, placeholder }: SettingsSelectProps) {
  return (
    <Select value={value} onValueChange={onChange} disabled={disabled}>
      <SelectTrigger className="w-full">
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {options.map((option) => (
          <SelectItem key={option.value} value={option.value}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
