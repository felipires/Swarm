import { MutationCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App";
import { toast } from "./components/ui/toast";
import { ToastViewport } from "./components/ui/toast";
import "./index.css";
import { getApiErrorMessage } from "./utils/apiError";

const queryClient = new QueryClient({
  // Surface the cluster's error message for every failed mutation, unless the
  // mutation opts out via meta.suppressErrorToast.
  mutationCache: new MutationCache({
    onError: (error, _vars, _ctx, mutation) => {
      if (mutation.meta?.suppressErrorToast) return;
      toast(getApiErrorMessage(error), "error");
    },
  }),
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      refetchOnWindowFocus: true,
      retry: 1,
    },
  },
});

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
      <ToastViewport />
    </QueryClientProvider>
  </React.StrictMode>,
);
