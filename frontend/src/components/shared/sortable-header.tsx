import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react';
import { Th } from '@/components/ui/surface';
import { cn } from '@/lib/utils';
import type { SortDirection } from '@/types/api';

interface SortableHeaderProps {
  field: string;
  label: string;
  sortBy: string;
  sortDirection: SortDirection;
  onSort: (field: string) => void;
  align?: 'left' | 'right';
}

export function SortableHeader({ field, label, sortBy, sortDirection, onSort, align = 'left' }: SortableHeaderProps) {
  const active = sortBy === field;
  const Icon = !active ? ArrowUpDown : sortDirection === 'Asc' ? ArrowUp : ArrowDown;
  return (
    <Th
      aria-sort={active ? (sortDirection === 'Asc' ? 'ascending' : 'descending') : 'none'}
      className={cn(align === 'right' && 'text-right')}
    >
      <button
        type="button"
        onClick={() => {
          onSort(field);
        }}
        className={cn('inline-flex items-center gap-1 uppercase hover:text-zinc-800', active && 'text-zinc-800')}
      >
        {label}
        <Icon className={cn('size-3.5', !active && 'opacity-40')} aria-hidden />
      </button>
    </Th>
  );
}
