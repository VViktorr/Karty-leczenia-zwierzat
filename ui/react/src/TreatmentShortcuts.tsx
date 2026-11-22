import {useHotkeys} from "react-hotkeys-hook";

export default function TreatmentShortcuts({back, next, summary, save}: {
  back?: () => void; next?: () => void; summary?: () => void; save?: () => void;
}) {
  useHotkeys("alt+left", () => back?.(), {enabled: !!back, preventDefault: true}, [back]);
  useHotkeys("alt+right", () => next?.(), {enabled: !!next, preventDefault: true}, [next]);
  useHotkeys("alt+enter", () => summary?.(), {enabled: !!summary, preventDefault: true}, [summary]);
  useHotkeys("mod+s", () => save?.(), {
    enabled: !!save, preventDefault: true, enableOnFormTags: ["INPUT", "TEXTAREA"],
  }, [save]);
  return null;
}
