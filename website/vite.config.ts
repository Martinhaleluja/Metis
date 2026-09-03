import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

import pkg from "./package.json";

export default defineConfig({
  plugins: [react(), tailwindcss()],

  // The site's version, so a support email and a Sentry release can both name
  // the build being complained about. Read from package.json rather than
  // written out a second time, because two copies of a version eventually
  // disagree and the wrong one is always the one you are reading.
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
  },
});
