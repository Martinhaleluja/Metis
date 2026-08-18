import { useEffect, useRef } from "react";
import { animate, useReducedMotion } from "motion/react";

/**
 * Counts from the previous figure to the new one. The tween writes straight to
 * the DOM node so a running count never re-renders the React tree.
 */
export function CountUp({ value }: { value: number }) {
  const ref = useRef<HTMLSpanElement>(null);
  const previous = useRef(0);
  const reduce = useReducedMotion();

  useEffect(() => {
    const node = ref.current;
    if (!node) return;

    if (reduce) {
      node.textContent = value.toLocaleString();
      previous.current = value;
      return;
    }

    const controls = animate(previous.current, value, {
      duration: 1.2,
      ease: [0.16, 1, 0.3, 1],
      onUpdate: (latest) => {
        node.textContent = Math.round(latest).toLocaleString();
      },
    });

    previous.current = value;
    return () => controls.stop();
  }, [value, reduce]);

  return (
    <span ref={ref} className="tabular-nums">
      0
    </span>
  );
}
