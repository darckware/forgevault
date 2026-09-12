/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        "vault-bg": {
          DEFAULT: "#0f172a",
          deep: "#020617",
        },
        "vault-surface": {
          DEFAULT: "#164e63",
          dim: "#0e2f3d",
          border: "#083344",
        },
        "vault-accent": {
          DEFAULT: "#2dd4bf",
          bright: "#5eead4",
          dark: "#0d9488",
        },
        "vault-danger": "#f43f5e",
        "vault-warning": "#f59e0b",
        "vault-ink": "#022c22",
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
