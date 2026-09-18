/** Money and date formatting shared by every page. */

export function formatMoney(amount: number | null | undefined, currency = "EUR"): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return "—";

  try {
    return new Intl.NumberFormat(undefined, { style: "currency", currency }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return "—";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(date);
}

export function formatDateRange(start?: string | null, end?: string | null): string {
  if (!start && !end) return "—";
  if (start && end) return `${formatDate(start)} → ${formatDate(end)}`;
  return formatDate(start ?? end);
}
