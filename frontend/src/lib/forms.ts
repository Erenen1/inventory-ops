import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError, errorMessage } from '@/lib/api';

/**
 * Copies server-side validation errors onto matching form fields. Returns a message for errors that
 * do not belong to a field (business rule violations, conflicts) so the form can show them inline.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): string | null {
  if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
    let unmatched = false;
    for (const [field, messages] of Object.entries(error.fieldErrors)) {
      const match = fields.find((f) => f.toLowerCase() === field.toLowerCase());
      if (match) setError(match, { type: 'server', message: messages[0] });
      else unmatched = true;
    }
    return unmatched ? error.message : null;
  }
  return errorMessage(error);
}
