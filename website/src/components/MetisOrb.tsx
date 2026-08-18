import { useReducedMotion } from "motion/react";

/**
 * The product mark with depth built around it. The PNG is the real icon
 * shipped with the Windows app rather than a redrawing of it; everything
 * animated sits behind and around that asset.
 *
 * The motion is doing a job: a companion that idles in your tray should look
 * alive but not busy, so this breathes slowly instead of spinning.
 */
export function MetisOrb({ className = "" }: { className?: string }) {
  const reduce = useReducedMotion();
  const halo = reduce ? "" : "animate-halo";
  const drift = reduce ? "" : "animate-drift";

  return (
    <div className={`relative grid place-items-center ${className}`} aria-hidden="true">
      {/* Two offset glow plates give the mark a soft cast on the page. */}
      <div
        className={`absolute h-[125%] w-[125%] rounded-full blur-3xl ${halo}`}
        style={{
          background:
            "radial-gradient(circle at 50% 45%, color-mix(in srgb, var(--sky) 55%, transparent), transparent 68%)",
        }}
      />
      <div
        className={`absolute h-[92%] w-[92%] rounded-full blur-2xl ${halo}`}
        style={{
          animationDelay: "-2.4s",
          background:
            "radial-gradient(circle at 50% 55%, color-mix(in srgb, var(--accent) 32%, transparent), transparent 70%)",
        }}
      />

      {/* Concentric rings read as the listening radius. */}
      <div
        className="absolute h-[108%] w-[108%] rounded-full border opacity-45"
        style={{ borderColor: "color-mix(in srgb, var(--sky) 55%, transparent)" }}
      />
      <div
        className="absolute h-[136%] w-[136%] rounded-full border opacity-25"
        style={{ borderColor: "color-mix(in srgb, var(--sky) 45%, transparent)" }}
      />

      <img
        src="/metis-mark.png"
        alt=""
        width={256}
        height={256}
        fetchPriority="high"
        decoding="async"
        className={`relative w-full max-w-[168px] drop-shadow-[0_18px_40px_rgba(10,107,224,0.28)] ${drift}`}
      />
    </div>
  );
}
