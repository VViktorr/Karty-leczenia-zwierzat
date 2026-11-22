import {expect, test} from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

test.beforeEach(async ({page}) => {
  await page.addInitScript(() => {
    const document = {
      id: 1, number: "FS 1/2026", date: "2026-11-22", payer: "Gospodarstwo testowe",
      shortPayer: "Gospodarstwo testowe", gross: 40, currency: "PLN", relatedText: "",
    };
    const treatmentRow = {
      index: 0, product: "Lek testowy", symbol: "MED-1", species: "Bydło", quantity: "1",
      animals: "2", batch: "S123", route: "IM", dosing: "Dawka testowa", withdrawal: "Wpis testowy",
      description: "Grupa testowa", indication: "Wskazanie testowe", diagnosis: "", section: "II", selected: true,
    };
    let receive: (event: {data: unknown}) => void;
    const calls: string[] = [];
    Object.assign(window, {
      reactklCalls: calls,
      chrome: {
        webview: {
          addEventListener(_: string, listener: typeof receive) { receive = listener; },
          postMessage(request: {action: string; id: string; _bridgeRequestId: string}) {
            calls.push(request.action);
            const values: Record<string, unknown> = {
              searchDocuments: {items: [document], loadedAt: "2026-11-22", cached: true},
              readDocument: {draftId: "1", number: document.number, payer: document.payer, rows: [treatmentRow]},
              treatmentSuggestions: {
                items: [{index: 0, symbol: "MED-1", status: "existingData", variantCount: 1, hasCatalogueVariants: true}],
                compatibility: [{index: 0, groups: ["cattle"], currentSpecies: "Bydło", currentGroups: ["cattle"]}],
              },
              previewDraft: {html: "<p>Karta robocza</p>"},
              saveDraft: {duplicate: false, id: "saved", number: "K/1/2026", html: "<p>K/1/2026</p>"},
              getProgramVersion: {version: "0.0.0"},
            };
            if (request.action !== "cancelRequest") queueMicrotask(() =>
              receive?.({data: {id: request._bridgeRequestId ?? request.id, ok: true, data: values[request.action]}}));
          },
        },
      },
    });
  });
});

test("a document reaches its preview and save using the keyboard", async ({page}) => {
  await page.goto("/");
  await page.getByText("FS 1/2026", {exact: true}).dblclick();
  await expect(page.getByRole("heading", {name: "Karta leczenia — FS 1/2026"})).toBeVisible();
  await expect(page.getByRole("button", {name: "Przejdź do podsumowania"})).toBeEnabled();
  await page.getByRole("heading", {name: "Karta leczenia — FS 1/2026"}).click();
  await page.keyboard.press("Alt+Enter");
  await expect(page.frameLocator('[title="Podsumowanie wydruku karty leczenia"]').getByText("Karta robocza")).toBeVisible();
  const calls = () => page.evaluate(() => (window as unknown as {reactklCalls: string[]}).reactklCalls);
  expect(await calls()).not.toContain("saveDraft");
  await page.keyboard.press("Control+s");
  await expect(page.getByText("Zapisano kartę K/1/2026")).toBeVisible();
  expect((await calls()).filter(action => action === "saveDraft")).toHaveLength(1);
});

test("appearance and shortcuts are configured in Settings", async ({page}) => {
  await page.goto("/");
  await page.getByRole("button", {name: "Ustawienia", exact: true}).click();
  await page.getByRole("tab", {name: "Wygląd", exact: true}).click();
  await expect(page.getByRole("combobox", {name: "Skórka", exact: true})).toBeVisible();
  const results = await new AxeBuilder({page}).include(".settings-appearance").withRules(["label", "aria-input-field-name"]).analyze();
  expect(results.violations).toEqual([]);
  await page.getByRole("tab", {name: "Karty leczenia", exact: true}).click();
  await page.getByRole("switch", {name: "Skróty klawiaturowe karty"}).click();
  await page.reload();
  await page.getByRole("button", {name: "Ustawienia", exact: true}).click();
  await expect(page.getByRole("switch", {name: "Skróty klawiaturowe karty"})).not.toBeChecked();
});
