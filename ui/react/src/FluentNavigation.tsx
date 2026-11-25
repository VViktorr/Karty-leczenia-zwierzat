import type {ReactElement} from "react";
import {Button} from "@fluentui/react-button";
import {FluentProvider} from "@fluentui/react-provider";
import {webDarkTheme, webLightTheme} from "@fluentui/react-theme";

export default function FluentNavigation<Section extends string>({items, current, palette, onNavigate}: {
  items: ReadonlyArray<{id: Section; name: string; icon?: ReactElement}>;
  current: Section;
  palette: "light" | "dark" | "sea";
  onNavigate: (section: Section) => void;
}) {
  return <FluentProvider className="fluent-nav-provider"
    theme={palette === "dark" ? webDarkTheme : webLightTheme}>
    <nav aria-label="Nawigacja Karty Leczenia" className="main-nav">
      {items.map(item => <Button key={item.id} type="button" size="medium"
        appearance="subtle" icon={item.icon} className="fluent-nav-button"
        aria-current={item.id === current ? "page" : undefined}
        onClick={() => onNavigate(item.id)}>{item.name}</Button>)}
    </nav>
  </FluentProvider>;
}
