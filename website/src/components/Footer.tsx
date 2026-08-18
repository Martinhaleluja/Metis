export function Footer() {
  return (
    <footer className="border-t border-line py-9">
      <div className="mx-auto flex max-w-[1180px] flex-col items-center justify-between gap-4 px-5 sm:flex-row">
        <div className="flex items-center gap-2">
          <img src="/metis-mark.png" alt="" width={22} height={22} className="h-[22px] w-[22px]" />
          <span className="font-display text-[15px] font-semibold text-ink">Metis</span>
        </div>

        <p className="text-[13px] text-ink-muted">
          An AI companion for your computer. Windows 10 and 11.
        </p>

        <a
          href="https://github.com/Martinhaleluja/Metis"
          className="text-[13px] text-ink-muted transition-colors duration-200 hover:text-ink"
        >
          Source
        </a>
      </div>
    </footer>
  );
}
