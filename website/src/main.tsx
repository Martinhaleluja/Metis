import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import "@fontsource-variable/outfit";
import "@fontsource-variable/work-sans";
import "./index.css";

import App from "./App";
import { startErrorReporting } from "./lib/telemetry";

// Before the first render, so a crash during it is still reported. Does nothing
// at all unless VITE_SENTRY_DSN is set, which today it is not.
startErrorReporting();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
