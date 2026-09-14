/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        // Every vault-* token and the built-in `slate` scale resolve through a CSS
        // variable (index.css: :root = light, .dark = dark) so the app-wide light/dark
        // toggle (src/lib/theme.tsx) doesn't require touching every call site — only the
        // variable definitions differ per theme. `rgb(var(--x) / <alpha-value>)` is the
        // documented Tailwind v3 pattern for keeping opacity modifiers (bg-vault-ink/40)
        // working with a variable-backed color.
        "vault-bg": {
          DEFAULT: "rgb(var(--vault-bg) / <alpha-value>)",
          deep: "rgb(var(--vault-bg-deep) / <alpha-value>)",
        },
        "vault-surface": {
          DEFAULT: "rgb(var(--vault-surface) / <alpha-value>)",
          dim: "rgb(var(--vault-surface-dim) / <alpha-value>)",
          border: "rgb(var(--vault-surface-border) / <alpha-value>)",
        },
        "vault-accent": {
          DEFAULT: "rgb(var(--vault-accent) / <alpha-value>)",
          bright: "rgb(var(--vault-accent-bright) / <alpha-value>)",
          dark: "rgb(var(--vault-accent-dark) / <alpha-value>)",
        },
        "vault-danger": "rgb(var(--vault-danger) / <alpha-value>)",
        "vault-warning": "rgb(var(--vault-warning) / <alpha-value>)",
        "vault-ink": "rgb(var(--vault-ink) / <alpha-value>)",
        slate: {
          100: "rgb(var(--tw-slate-100) / <alpha-value>)",
          200: "rgb(var(--tw-slate-200) / <alpha-value>)",
          300: "rgb(var(--tw-slate-300) / <alpha-value>)",
          400: "rgb(var(--tw-slate-400) / <alpha-value>)",
          500: "rgb(var(--tw-slate-500) / <alpha-value>)",
          600: "rgb(var(--tw-slate-600) / <alpha-value>)",
          700: "rgb(var(--tw-slate-700) / <alpha-value>)",
        },
      },
      fontFamily: {
        mono: ['"JetBrains Mono"', "ui-monospace", "SFMono-Regular", "monospace"],
      },
      boxShadow: {
        "vault-glow": "0 0 0 1px rgba(45,212,191,0.25), 0 0 24px rgba(45,212,191,0.15)",
      },
    },
  },
  plugins: [],
};
