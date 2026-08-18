/**
 * Regenerates src/lib/appIcons.ts from the simple-icons package.
 *
 * The marquee needs about twenty glyphs. Importing them from the package at
 * runtime would pull the whole set into the bundle, so the path data is baked
 * into a small module instead and simple-icons stays a devDependency.
 *
 * Run with: node scripts/generate-app-icons.cjs
 */
const fs = require("fs");
const path = require("path");

// Complex desktop software where being shown the way actually helps. Brands
// that Simple Icons removed for trademark reasons (Microsoft 365, Adobe CC,
// Slack, Canva) are simply absent rather than redrawn by hand.
const slugs = [
  "blender", "davinciresolve", "krita", "inkscape", "gimp", "audacity",
  "obsstudio", "sketchup", "autodeskmaya", "autodeskrevit", "unity", "figma",
  "libreoffice", "notion", "airtable", "trello", "quickbooks", "xero",
  "wordpress", "shopify",
];

const iconsDir = path.join(__dirname, "..", "node_modules", "simple-icons", "icons");

const rows = slugs.map((slug) => {
  const svg = fs.readFileSync(path.join(iconsDir, `${slug}.svg`), "utf8");
  const title = /<title>([^<]+)<\/title>/.exec(svg)[1];
  const d = /<path\s+d="([^"]+)"/.exec(svg)[1];
  return { slug, title, d };
});

const header = `// Generated from the simple-icons package. Do not edit by hand; re-run
// scripts/generate-app-icons.cjs to refresh.
//
// The glyphs are CC0 from Simple Icons. The marks themselves remain the
// trademarks of their respective owners and appear here only to name the
// software Metis can walk you through.

export type AppIcon = { slug: string; title: string; d: string };

export const appIcons: AppIcon[] = [
`;

const body = rows
  .map((r) => `  { slug: ${JSON.stringify(r.slug)}, title: ${JSON.stringify(r.title)}, d: ${JSON.stringify(r.d)} },`)
  .join("\n");

fs.writeFileSync(path.join(__dirname, "..", "src", "lib", "appIcons.ts"), `${header}${body}\n];\n`, "utf8");

console.log(`wrote src/lib/appIcons.ts with ${rows.length} icons`);
console.log(rows.map((r) => r.title).join(", "));
