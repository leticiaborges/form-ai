import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { AuthProvider } from "./context/AuthProvider";
import App from "./App";
import "./index.css";
import { Toaster } from "sonner";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error('Root element <div id="root"> not found in index.html');
}

createRoot(rootElement).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App></App>
        <Toaster
          position="top-right"
          richColors
          closeButton
          duration={4000}
          theme="light"
        ></Toaster>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
