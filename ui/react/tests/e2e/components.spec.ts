import {expect, test} from "@playwright/test";

test("Components settings persist PDF, shortcuts and layout independently", async ({page}) => {
  await page.goto("/");
  await page.getByRole("button",{name:"Ustawienia",exact:true}).click();
  await page.getByRole("tab",{name:"Komponenty"}).click();
  await expect(page.getByRole("combobox",{name:"Tryb przeglądania PDF"})).toBeVisible();
  await page.getByRole("combobox",{name:"Tryb przeglądania PDF"}).click();
  await page.getByText("Jedna strona — poprzednia / następna",{exact:true}).click();
  await page.getByRole("combobox",{name:"Domyślne powiększenie PDF"}).click();
  await page.getByText("125%",{exact:true}).click();
  await page.getByRole("combobox",{name:"Następny etap kreatora"}).click();
  await page.getByText("alt+shift+right",{exact:true}).click();
  await page.getByRole("tab",{name:"Wygląd"}).click();
  await page.getByRole("combobox",{name:"Położenie menu"}).click();
  await page.getByText("Menu górne",{exact:true}).click();
  await expect(page.locator(".app-shell")).toHaveClass(/nav-top/);
  await page.reload();
  await expect(page.locator(".app-shell")).toHaveClass(/nav-top/);
  await page.getByRole("button",{name:"Ustawienia",exact:true}).click();
  await page.getByRole("tab",{name:"Komponenty"}).click();
  const prefs=await page.evaluate(()=>
    JSON.parse(localStorage.getItem("ReactKL.componentPreferences.v1")??"null"));
  expect(prefs).toMatchObject({
    pdf:{mode:"pages",zoom:125},
    shortcuts:{next:"alt+shift+right"},
    navigation:"top",
  });
});

test("every skin supports both top and side menu",async ({page})=>{
  await page.goto("/");
  await page.getByRole("button",{name:"Ustawienia",exact:true}).click();
  await page.getByRole("tab",{name:"Wygląd"}).click();
  for(const skin of ["Nordic — jasny i spokojny","Forest — naturalny","Sandstone — ciepły","Midnight — kontrastowy"]){
    await page.getByRole("combobox",{name:"Skórka",exact:true}).click();
    await page.getByText(skin,{exact:true}).click();
    for(const [position,klass] of [["Menu górne","nav-top"],["Menu boczne","nav-side"]] as const){
      await page.getByRole("combobox",{name:"Położenie menu"}).click();
      await page.getByText(position,{exact:true}).click();
      await expect(page.locator(".app-shell")).toHaveClass(new RegExp(klass));
      await expect(page.getByRole("button",{name:"Dokumenty",exact:true})).toBeVisible();
    }
  }
});

test("AG Grid settings apply globally without per-list toggle in archived cards",async ({page})=>{
  await page.goto("/");
  await page.getByRole("button",{name:"Ustawienia",exact:true}).click();
  await page.getByRole("tab",{name:"Komponenty"}).click();
  await page.getByRole("combobox",{name:"Domyślny silnik tabel"}).click();
  await page.getByText("AG Grid — zaawansowany",{exact:true}).click();
  await page.getByRole("combobox",{name:"AG Grid — wysokość wiersza"}).click();
  await page.getByText("44 px",{exact:true}).click();
  await page.getByText("Filtry kolumn AG Grid",{exact:true}).click();
  await page.reload();
  const prefs=await page.evaluate(()=>
    JSON.parse(localStorage.getItem("ReactKL.componentPreferences.v1")??"null"));
  expect(prefs).toMatchObject({table:{mode:"ag"},agGrid:{rowHeight:44,filters:true}});
  await page.getByRole("button",{name:"Zapisane karty",exact:true}).click();
  await expect(page.locator(".kl-grid-toggle")).toHaveCount(0);
  await page.getByRole("button",{name:"Dokumenty",exact:true}).click();
  await expect(page.locator(".kl-grid-toggle")).toHaveCount(0);
});
