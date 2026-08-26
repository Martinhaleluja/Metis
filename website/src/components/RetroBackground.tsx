const items = [
  // Cursors
  { x: 5, y: 12, rotate: -15, opacity: 0.14, size: 32, type: "cursor" },
  { x: 88, y: 8, rotate: 20, opacity: 0.12, size: 28, type: "cursor" },
  { x: 42, y: 55, rotate: -8, opacity: 0.1, size: 26, type: "cursor" },
  { x: 72, y: 78, rotate: 12, opacity: 0.12, size: 30, type: "cursor" },
  { x: 60, y: 3, rotate: -22, opacity: 0.11, size: 24, type: "cursor" },
  { x: 20, y: 90, rotate: 18, opacity: 0.1, size: 28, type: "cursor" },
  // Floppy disks
  { x: 15, y: 35, rotate: 10, opacity: 0.13, size: 34, type: "floppy" },
  { x: 82, y: 42, rotate: -12, opacity: 0.11, size: 30, type: "floppy" },
  { x: 50, y: 88, rotate: 5, opacity: 0.12, size: 32, type: "floppy" },
  { x: 96, y: 15, rotate: -18, opacity: 0.1, size: 28, type: "floppy" },
  // Folders
  { x: 25, y: 65, rotate: -6, opacity: 0.13, size: 32, type: "folder" },
  { x: 68, y: 18, rotate: 8, opacity: 0.11, size: 28, type: "folder" },
  { x: 92, y: 60, rotate: -10, opacity: 0.12, size: 30, type: "folder" },
  { x: 38, y: 25, rotate: 14, opacity: 0.1, size: 26, type: "folder" },
  // Windows
  { x: 8, y: 80, rotate: 4, opacity: 0.09, size: 40, type: "window" },
  { x: 78, y: 30, rotate: -5, opacity: 0.08, size: 36, type: "window" },
  { x: 35, y: 15, rotate: 7, opacity: 0.09, size: 38, type: "window" },
  { x: 55, y: 95, rotate: -3, opacity: 0.08, size: 34, type: "window" },
  // Keyboard keys
  { x: 55, y: 70, rotate: -12, opacity: 0.12, size: 26, type: "key" },
  { x: 18, y: 50, rotate: 15, opacity: 0.11, size: 24, type: "key" },
  { x: 95, y: 90, rotate: -8, opacity: 0.12, size: 28, type: "key" },
  { x: 45, y: 40, rotate: 6, opacity: 0.1, size: 22, type: "key" },
  // Binary
  { x: 62, y: 5, rotate: 3, opacity: 0.09, size: 15, type: "binary" },
  { x: 30, y: 92, rotate: -4, opacity: 0.08, size: 14, type: "binary" },
  { x: 3, y: 45, rotate: 6, opacity: 0.09, size: 15, type: "binary" },
  { x: 85, y: 55, rotate: -2, opacity: 0.08, size: 14, type: "binary" },
  { x: 75, y: 95, rotate: 5, opacity: 0.09, size: 13, type: "binary" },
  { x: 12, y: 7, rotate: -7, opacity: 0.08, size: 14, type: "binary" },
  // Hourglasses
  { x: 10, y: 72, rotate: 8, opacity: 0.13, size: 30, type: "hourglass" },
  { x: 65, y: 38, rotate: -14, opacity: 0.11, size: 26, type: "hourglass" },
  { x: 90, y: 82, rotate: 5, opacity: 0.12, size: 28, type: "hourglass" },
  // Mouse
  { x: 30, y: 48, rotate: -10, opacity: 0.12, size: 30, type: "mouse" },
  { x: 80, y: 68, rotate: 16, opacity: 0.1, size: 26, type: "mouse" },
  { x: 48, y: 12, rotate: -5, opacity: 0.11, size: 28, type: "mouse" },
  // Recycle bin
  { x: 93, y: 25, rotate: 6, opacity: 0.11, size: 32, type: "recycle" },
  { x: 7, y: 58, rotate: -8, opacity: 0.1, size: 28, type: "recycle" },
  // CD
  { x: 22, y: 82, rotate: 12, opacity: 0.1, size: 34, type: "cd" },
  { x: 75, y: 10, rotate: -20, opacity: 0.09, size: 30, type: "cd" },
  { x: 52, y: 62, rotate: 8, opacity: 0.1, size: 32, type: "cd" },
  // Error dialog
  { x: 40, y: 75, rotate: -3, opacity: 0.09, size: 42, type: "error" },
  { x: 85, y: 48, rotate: 4, opacity: 0.08, size: 38, type: "error" },
  // Monitor
  { x: 14, y: 22, rotate: -6, opacity: 0.1, size: 36, type: "monitor" },
  { x: 70, y: 52, rotate: 10, opacity: 0.09, size: 32, type: "monitor" },
  // Start button
  { x: 33, y: 60, rotate: -4, opacity: 0.1, size: 20, type: "start" },
  { x: 88, y: 72, rotate: 7, opacity: 0.09, size: 18, type: "start" },
];

function CursorSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor">
      <path d="M4 2l14 11.5-5.5 1.2L17 22l-3.5 1L9 15.5 4.5 20z" />
    </svg>
  );
}

function FloppySvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="3" y="2" width="18" height="20" rx="1" />
      <rect x="7" y="2" width="10" height="7" />
      <rect x="7" y="14" width="10" height="8" />
      <line x1="9" y1="16" x2="15" y2="16" />
      <line x1="9" y1="18" x2="15" y2="18" />
    </svg>
  );
}

function FolderSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M2 6a2 2 0 012-2h5l2 2h9a2 2 0 012 2v10a2 2 0 01-2 2H4a2 2 0 01-2-2V6z" />
    </svg>
  );
}

function WindowSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="2" y="3" width="20" height="18" rx="1" />
      <line x1="2" y1="8" x2="22" y2="8" />
      <circle cx="5" cy="5.5" r="1" fill="currentColor" />
      <circle cx="8" cy="5.5" r="1" fill="currentColor" />
    </svg>
  );
}

function KeySvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <rect x="6" y="8" width="4" height="3" rx="0.5" />
      <rect x="12" y="8" width="4" height="3" rx="0.5" />
      <rect x="8" y="13" width="8" height="3" rx="0.5" />
    </svg>
  );
}

function BinaryText({ size }: { size: number }) {
  return (
    <span
      style={{
        fontSize: size,
        fontFamily: "Consolas, monospace",
        letterSpacing: "0.05em",
      }}
    >
      10110
    </span>
  );
}

function HourglassSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M5 3h14M5 21h14" />
      <path d="M7 3v4l5 5-5 5v4M17 3v4l-5 5 5 5v4" />
      <path d="M10 12h4" />
    </svg>
  );
}

function MouseSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="6" y="2" width="12" height="20" rx="6" />
      <line x1="12" y1="2" x2="12" y2="10" />
      <line x1="12" y1="6" x2="12" y2="8" strokeWidth="2.5" />
    </svg>
  );
}

function RecycleSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M4 7h16M10 11v6M14 11v6" />
      <path d="M5 7l1 12a2 2 0 002 2h8a2 2 0 002-2l1-12" />
      <path d="M9 7V4a1 1 0 011-1h4a1 1 0 011 1v3" />
    </svg>
  );
}

function CdSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <circle cx="12" cy="12" r="10" />
      <circle cx="12" cy="12" r="3" />
      <path d="M12 2a10 10 0 014 1" strokeDasharray="2 3" />
    </svg>
  );
}

function ErrorSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="1" y="4" width="22" height="16" rx="1" />
      <line x1="1" y1="8" x2="23" y2="8" />
      <circle cx="12" cy="14" r="2" fill="currentColor" />
      <line x1="12" y1="10.5" x2="12" y2="12" strokeWidth="2" />
      <rect x="18" y="5" width="3" height="2" rx="0.5" fill="currentColor" opacity="0.4" />
    </svg>
  );
}

function MonitorSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="2" y="3" width="20" height="14" rx="1" />
      <line x1="8" y1="21" x2="16" y2="21" />
      <line x1="12" y1="17" x2="12" y2="21" />
      <line x1="5" y1="7" x2="11" y2="7" strokeDasharray="2 2" opacity="0.5" />
      <line x1="5" y1="10" x2="9" y2="10" strokeDasharray="2 2" opacity="0.5" />
    </svg>
  );
}

function StartSvg({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor">
      <rect x="1" y="1" width="10" height="10" rx="1" opacity="0.8" />
      <rect x="13" y="1" width="10" height="10" rx="1" opacity="0.6" />
      <rect x="1" y="13" width="10" height="10" rx="1" opacity="0.6" />
      <rect x="13" y="13" width="10" height="10" rx="1" opacity="0.4" />
    </svg>
  );
}

const renderers: Record<string, (s: number) => JSX.Element> = {
  cursor: (s) => <CursorSvg size={s} />,
  floppy: (s) => <FloppySvg size={s} />,
  folder: (s) => <FolderSvg size={s} />,
  window: (s) => <WindowSvg size={s} />,
  key: (s) => <KeySvg size={s} />,
  binary: (s) => <BinaryText size={s} />,
  hourglass: (s) => <HourglassSvg size={s} />,
  mouse: (s) => <MouseSvg size={s} />,
  recycle: (s) => <RecycleSvg size={s} />,
  cd: (s) => <CdSvg size={s} />,
  error: (s) => <ErrorSvg size={s} />,
  monitor: (s) => <MonitorSvg size={s} />,
  start: (s) => <StartSvg size={s} />,
};

export function RetroBackground() {
  return (
    <div
      className="pointer-events-none fixed inset-0 z-0 overflow-hidden"
      aria-hidden="true"
    >
      {items.map((item, i) => (
        <div
          key={i}
          className="absolute text-ink"
          style={{
            left: `${item.x}%`,
            top: `${item.y}%`,
            transform: `rotate(${item.rotate}deg)`,
            opacity: item.opacity,
          }}
        >
          {renderers[item.type](item.size)}
        </div>
      ))}
    </div>
  );
}
