"use client";

import { createContext, useContext, useEffect, useState } from "react";

type Theme = "light" | "dark" | "system";
type ResolvedTheme = "light" | "dark";

type ThemeContextValue = {
  /** Current preference. "system" follows OS. */
  theme: Theme;
  /** Concrete value applied to <html> right now. */
  resolvedTheme: ResolvedTheme;
  setTheme: (t: Theme) => void;
  toggle: () => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

const STORAGE_KEY = "ojunai-theme";

function readStoredTheme(): Theme {
  // Default to "dark" so the app matches the marketing site (ojunai.com) for
  // anyone who hasn't explicitly picked a theme. Existing users with an explicit
  // preference saved in localStorage still get what they chose.
  if (typeof window === "undefined") return "dark";
  const v = window.localStorage.getItem(STORAGE_KEY);
  return v === "dark" || v === "light" || v === "system" ? v : "dark";
}

function systemPrefersDark(): boolean {
  if (typeof window === "undefined") return false;
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

function applyClass(resolved: ResolvedTheme) {
  if (typeof document === "undefined") return;
  const root = document.documentElement;
  if (resolved === "dark") root.classList.add("dark");
  else root.classList.remove("dark");
  // Hint to UA for native form controls / scrollbars
  root.style.colorScheme = resolved;
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<Theme>("dark");
  const [resolvedTheme, setResolvedTheme] = useState<ResolvedTheme>("dark");

  // Initial sync (after mount; the inline <head> script already set the class to avoid FOUC)
  useEffect(() => {
    const stored = readStoredTheme();
    setThemeState(stored);
    const resolved: ResolvedTheme =
      stored === "system" ? (systemPrefersDark() ? "dark" : "light") : stored;
    setResolvedTheme(resolved);
    applyClass(resolved);
  }, []);

  // Listen for OS pref changes when in "system" mode
  useEffect(() => {
    if (theme !== "system" || typeof window === "undefined") return;
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => {
      const resolved: ResolvedTheme = mq.matches ? "dark" : "light";
      setResolvedTheme(resolved);
      applyClass(resolved);
    };
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, [theme]);

  const setTheme = (t: Theme) => {
    setThemeState(t);
    window.localStorage.setItem(STORAGE_KEY, t);
    const resolved: ResolvedTheme =
      t === "system" ? (systemPrefersDark() ? "dark" : "light") : t;
    setResolvedTheme(resolved);
    applyClass(resolved);
  };

  // Two-state cycle for the simple button: dark <-> light. "system" reachable via menu later.
  const toggle = () => setTheme(resolvedTheme === "dark" ? "light" : "dark");

  return (
    <ThemeContext.Provider value={{ theme, resolvedTheme, setTheme, toggle }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used inside ThemeProvider");
  return ctx;
}

/**
 * Inline script to run before React hydrates, so the correct class is on <html>
 * before any paint. Prevents the "flash of wrong theme" on first load.
 *
 * Inject via <Script strategy="beforeInteractive"> or as a literal <script> in <head>.
 */
export const themeBootScript = `
(function(){
  try {
    var k = "ojunai-theme";
    var v = localStorage.getItem(k);
    // Default to "dark" so first-time visitors match the marketing site.
    // Only resolve to "system" / "light" when the user has explicitly opted in.
    var resolved = v === "light"
      ? "light"
      : v === "system"
        ? (window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")
        : "dark";
    if (resolved === "dark") document.documentElement.classList.add("dark");
    document.documentElement.style.colorScheme = resolved;
  } catch (_) { document.documentElement.classList.add("dark"); }
})();
`;
