export function rplEntryFromPdfName(name: string): string | undefined {
  return /^(?:ulotka|charakterystyka)[-_](\d+)[-_]\d{4}-\d{2}-\d{2}(?=[-_.]|$)/i.exec(name.trim())?.[1];
}

export function compactWithdrawal(tissues: string, milk: string, eggs = ""): string {
  const compact = (text: string) => text.trim()
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean)
    .join(" ")
    .replace(/\s*[●•▪]\s*/g, "; ")
    .replace(/^;\s*/, "");
  const tissueText = compact(tissues);
  const milkText = compact(milk);
  const eggText = compact(eggs);
  return [tissueText ? "tk. " + tissueText : "", milkText ? "ml. " + milkText : "", eggText ? "jaja: " + eggText : ""]
    .filter(Boolean).join(" · ");
}

export function isPoultry(species: string): boolean {
  return /(?:^|[^\p{L}])(?:drób|drobi|kur|brojler|niosk|indyk|indycz|kaczk|gęś|gęsi|przepiór|kuropatw)/iu.test(species);
}
