import {useHotkeys} from "react-hotkeys-hook";
import {useComponents} from "./componentSettings";

export default function TreatmentShortcuts({back, next, summary, save}: {
  back?: () => void; next?: () => void; summary?: () => void; save?: () => void;
}) {
  const {shortcuts} = useComponents();
  useHotkeys(shortcuts.back, () => back?.(),
    {enabled:shortcuts.enabled && !!back, preventDefault:true}, [back,shortcuts.enabled,shortcuts.back]);
  useHotkeys(shortcuts.next, () => next?.(),
    {enabled:shortcuts.enabled && !!next, preventDefault:true}, [next,shortcuts.enabled,shortcuts.next]);
  useHotkeys(shortcuts.summary, () => summary?.(),
    {enabled:shortcuts.enabled && !!summary, preventDefault:true}, [summary,shortcuts.enabled,shortcuts.summary]);
  useHotkeys(shortcuts.save, () => save?.(), {
    enabled: shortcuts.enabled && !!save, preventDefault:true,
    enableOnFormTags: ["INPUT","TEXTAREA"],
  }, [save,shortcuts.enabled,shortcuts.save]);
  return null;
}
