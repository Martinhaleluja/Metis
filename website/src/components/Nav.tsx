const links = [
  { href: "#capabilities", label: "What it does" },
  { href: "#how", label: "How it works" },
  { href: "#privacy", label: "Privacy" },
];

export function Nav() {
  return (
    <header className="fixed inset-x-0 top-0 z-50">
      <div className="mx-auto flex h-[68px] max-w-[1180px] items-center justify-between gap-6 px-5">
        <div className="material flex h-[52px] w-full items-center justify-between gap-6 rounded-full px-4 sm:px-5">
          <a href="#top" className="flex shrink-0 items-center gap-2">
            <img src="/metis-mark.png" alt="" width={26} height={26} className="h-[26px] w-[26px]" />
            <span className="font-display text-[17px] font-semibold tracking-tight text-ink">
              Metis
            </span>
          </a>

          <nav aria-label="Sections" className="hidden items-center gap-7 md:flex">
            {links.map((link) => (
              <a
                key={link.href}
                href={link.href}
                className="text-[14px] text-ink-muted transition-colors duration-200 hover:text-ink"
              >
                {link.label}
              </a>
            ))}
          </nav>

          <a
            href="#join"
            className="press shrink-0 rounded-full bg-accent px-4 py-2 text-[14px] font-medium whitespace-nowrap text-accent-contrast hover:bg-accent-hover"
          >
            Join the waitlist
          </a>
        </div>
      </div>
    </header>
  );
}
