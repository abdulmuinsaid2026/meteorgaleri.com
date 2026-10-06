/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Views/**/*.cshtml",
    "./Areas/**/*.cshtml",
    "./wwwroot/js/**/*.js",
    "./wwwroot/js/*.js",
    "./*.cshtml"
  ],
  theme: {
    extend: {
      colors: {
        meteor: {
          blue: "#00A4D3",
          "blue-dark": "#0284C7",
          "blue-light": "#E0F2FE",
          red: "#E30613",
          "red-dark": "#DC2626",
          "red-light": "#FEE2E2",
          dark: "#1E293B",
          slate: "#0F172A",
          muted: "#64748B",
          bg: "#F8FAFC",
          white: "#FFFFFF"
        },
        canvasia: {
          ink: "#1E293B",
          forest: "#00A4D3",
          "forest-deep": "#0284C7",
          gold: "#E30613",
          sand: "#F8FAFC",
          mist: "#E2E8F0",
          cream: "#FFFFFF"
        }
      },
      fontFamily: {
        heading: ["Plus Jakarta Sans", "system-ui", "sans-serif"],
        body: ["Inter", "system-ui", "sans-serif"],
        serif: ["Plus Jakarta Sans", "system-ui", "sans-serif"]
      },
      boxShadow: {
        soft: "0 18px 45px rgba(30, 41, 59, 0.08)",
        panel: "0 24px 60px rgba(0, 164, 211, 0.10)"
      }
    }
  },
  plugins: []
};
