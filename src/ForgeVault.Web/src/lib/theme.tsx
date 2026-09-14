import * as React from "react";

// Mirrors ForgeHub's lib/theme.tsx exactly (Theme union, resolvedTheme, matchMedia listener
// for "system") — the light/dark values themselves are ForgeVault's own vault-* palette
// (index.css), never ForgeHub's shadcn/ui tokens.
type Theme = "light" | "dark" | "system";

const STORAGE_KEY = "forgevault-theme";

interface ThemeContextValue {
  theme: Theme;
  resolvedTheme: "light" | "dark";
  setTheme: (theme: Theme) => void;
}

const ThemeContext = React.createContext<ThemeContextValue | undefined>(undefined);

function getSystemTheme(): "light" | "dark" {
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function applyTheme(resolved: "light" | "dark") {
  document.documentElement.classList.toggle("dark", resolved === "dark");
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = React.useState<Theme>(() => {
    const stored = localStorage.getItem(STORAGE_KEY) as Theme | null;
    // Default "dark", not "system" (unlike ForgeHub) — ForgeVault has only ever rendered
    // dark until this revision, so a fresh/never-chosen preference should keep looking the
    // way every existing user already knows it, not silently flip to light for anyone whose
    // OS happens to prefer light.
    return stored ?? "dark";
  });

  const resolvedTheme = theme === "system" ? getSystemTheme() : theme;

  React.useEffect(() => {
    applyTheme(resolvedTheme);
  }, [resolvedTheme]);

  React.useEffect(() => {
    if (theme !== "system") return;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const listener = () => applyTheme(getSystemTheme());
    media.addEventListener("change", listener);
    return () => media.removeEventListener("change", listener);
  }, [theme]);

  const setTheme = React.useCallback((next: Theme) => {
    localStorage.setItem(STORAGE_KEY, next);
    setThemeState(next);
  }, []);

  return <ThemeContext.Provider value={{ theme, resolvedTheme, setTheme }}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const ctx = React.useContext(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used within a ThemeProvider");
  return ctx;
}
