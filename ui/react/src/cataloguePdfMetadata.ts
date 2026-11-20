export function rplEntryFromPdfName(name: string): string | undefined {
  return /^(?:ulotka|charakterystyka)[-_](\d+)[-_]\d{4}-\d{2}-\d{2}(?=[-_.]|$)/i.exec(name.trim())?.[1];
}

export function compactWithdrawal(tissues: string, milk: string): string {
  const compact = (text: string) => text.trim()
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean)
    .join(" ")
    .replace(/\s*[●•▪]\s*/g, "; ")
    .replace(/^;\s*/, "");
  const tissueText = compact(tissues);
  const milkText = compact(milk);
  return [tissueText ? "tk. " + tissueText : "", milkText ? "ml. " + milkText : ""]
    .filter(Boolean).join(" · ");
}
