import { appIcons } from "../lib/appIcons";

/**
 * A continuous strip of the software Metis can walk you through.
 *
 * The list is rendered twice inside one track: the animation translates the
 * track by exactly half its width, which lands on an identical frame, so the
 * loop has no visible seam. Only the first copy is exposed to assistive tech.
 *
 * Hovering or tab-focusing the strip pauses it, so wanting to read a logo is
 * enough to stop it moving.
 */
export function AppMarquee() {
  return (
    <section className="py-14 sm:py-16" aria-labelledby="marquee-heading">
      <div className="mx-auto max-w-[1180px] px-5">
        <h2
          id="marquee-heading"
          className="type-caption text-center text-ink-muted"
        >
          Learn the software you never got shown
        </h2>
      </div>

      <div
        className="marquee relative mt-9 overflow-hidden"
        style={{
          // Fade both ends into the page instead of cutting the strip off.
          maskImage:
            "linear-gradient(to right, transparent, #000 9%, #000 91%, transparent)",
          WebkitMaskImage:
            "linear-gradient(to right, transparent, #000 9%, #000 91%, transparent)",
        }}
      >
        <div className="marquee-track flex w-max items-center gap-14 pr-14 sm:gap-20 sm:pr-20">
          {[0, 1].map((copy) => (
            <ul
              key={copy}
              className="flex shrink-0 items-center gap-14 sm:gap-20"
              aria-hidden={copy === 1 || undefined}
            >
              {appIcons.map((icon) => (
                <li key={`${copy}-${icon.slug}`} className="shrink-0">
                  <span className="group flex items-center gap-3">
                    <svg
                      role="img"
                      viewBox="0 0 24 24"
                      width={26}
                      height={26}
                      aria-hidden="true"
                      className="shrink-0 fill-ink-muted opacity-60 transition-[fill,opacity] duration-300 group-hover:fill-accent group-hover:opacity-100"
                    >
                      <path d={icon.d} />
                    </svg>
                    <span className="type-caption whitespace-nowrap text-ink-muted transition-colors duration-300 group-hover:text-ink">
                      {icon.title}
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          ))}
        </div>
      </div>
    </section>
  );
}
