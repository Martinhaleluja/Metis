/**
 * Regenerates src/lib/providerIcons.ts from the simple-icons package.
 *
 * Same reasoning as generate-app-icons.cjs: baking a handful of path strings
 * into a module keeps simple-icons a devDependency instead of shipping the
 * whole set to the browser.
 *
 * OpenAI is deliberately absent. Simple Icons removed it at the trademark
 * holder's request, and a mark redrawn by hand to get around that is worse than
 * no mark at all — the Providers section names OpenAI in type instead.
 *
 * Run with: node scripts/generate-provider-icons.cjs
 */
const fs = require("fs");
const path = require("path");

const slugs = ["googlegemini", "anthropic", "mistralai", "openrouter", "ollama"];

const iconsDir = path.join(__dirname, "..", "node_modules", "simple-icons", "icons");

const rows = slugs.map((slug) => {
  const svg = fs.readFileSync(path.join(iconsDir, `${slug}.svg`), "utf8");
  const title = /<title>([^<]+)<\/title>/.exec(svg)[1];
  const d = /<path\s+d="([^"]+)"/.exec(svg)[1];
  return { slug, title, d };
});

const header = `// Generated from the simple-icons package. Do not edit by hand; re-run
// scripts/generate-provider-icons.cjs to refresh.
//
// The glyphs are CC0 from Simple Icons. The marks themselves remain the
// trademarks of their respective owners and appear here only to name the AI
// providers Metis can talk to. OpenAI has no entry on purpose: Simple Icons
// removed that mark for trademark reasons, so the site names it in type.

export type ProviderIcon = { slug: string; title: string; d: string };

export const providerIcons: Record<string, ProviderIcon> = {
`;

const body = rows
  .map(
    (r) =>
      `  ${JSON.stringify(r.slug)}: { slug: ${JSON.stringify(r.slug)}, title: ${JSON.stringify(r.title)}, d: ${JSON.stringify(r.d)} },`,
  )
  .join("\n");

fs.writeFileSync(
  path.join(__dirname, "..", "src", "lib", "providerIcons.ts"),
  `${header}${body}\n};\n`,
  "utf8",
);

console.log(`wrote src/lib/providerIcons.ts with ${rows.length} icons`);
console.log(rows.map((r) => r.title).join(", "));
