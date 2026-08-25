import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./src/pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        background: "var(--background)",
        foreground: "var(--foreground)",
        ink: {
          50:  "#f4f1ea",
          100: "#e7e2d6",
          200: "#bbb6ab",
          300: "#7d7a72",
          400: "#4a4844",
          500: "#262626",
          600: "#1c1c1e",
          700: "#161618",
          800: "#101012",
          900: "#0a0a0c",
        },
        coral: {
          50:  "#fff1ec",
          100: "#ffd9cb",
          300: "#ff8c6f",
          500: "#ff5b3a",
          600: "#ea4624",
          700: "#bf3417",
        },
        sand: "#d9b97a",
        slate2: "#7d8a99",
        olive: "#9ba66a",
      },
      fontFamily: {
        display: ["Georgia", "Times New Roman", "serif"],
        sans: ["var(--font-sans)", "system-ui", "sans-serif"],
        mono: ["var(--font-mono)", "ui-monospace", "monospace"],
      },
      borderRadius: {
        none: "0",
        sm: "6px",
        DEFAULT: "8px",
        md: "10px",
        lg: "14px",
        xl: "18px",
        "2xl": "24px",
        full: "9999px",
      },
      boxShadow: {
        soft: "0 1px 0 rgba(255,255,255,0.04) inset, 0 8px 24px -12px rgba(0,0,0,0.6)",
        hard: "0 0 0 1px rgba(255,255,255,0.06), 0 18px 40px -16px rgba(0,0,0,0.8)",
        coral: "0 10px 30px -12px rgba(255,91,58,0.45)",
      },
      letterSpacing: {
        tightish: "-0.018em",
        wideish: "0.04em",
      },
      keyframes: {
        rise: {
          "0%": { transform: "translateY(6px)", opacity: "0" },
          "100%": { transform: "translateY(0)", opacity: "1" },
        },
      },
      animation: {
        rise: "rise 0.6s cubic-bezier(0.2, 0.7, 0.2, 1) both",
      },
    },
  },
  plugins: [],
};
export default config;
